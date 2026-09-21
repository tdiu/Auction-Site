# Concurrency handling

**Scope:** the races in the bid and payment paths, and the mechanism guarding each
**Companion docs:** [claim-and-lease.md](claim-and-lease.md) for the outbox dispatcher's own concurrency contract

## Overview

Concurrency is defended almost entirely at the **database layer**, with optimistic concurrency
tokens and unique or partial indexes, rather than with application locks. Each scenario below has
its own mechanism.

---

## 1. Concurrent bids on the same auction: optimistic concurrency token

**Mechanism:** `[ConcurrencyCheck]` on `Auction.CurrentHighBid`.

In `BidService.PlaceBid` the flow is read → validate → mutate → save:

- It reads the auction, runs the business guards (auction ended, self-bid,
  already-high-bidder, bid too low), then sets `CurrentHighBid`/`CurrentHighBidderId`
  and calls `CompleteAsync()`.
- Because `CurrentHighBid` is a concurrency token, EF emits
  `UPDATE ... WHERE AuctionId = @id AND CurrentHighBid = @originalValue`. If a competing
  bid committed in the gap between the read and the save, the original value no longer
  matches, **0 rows are affected**, and EF throws `DbUpdateConcurrencyException`.
- That is caught and returned as a `Conflict`
  ("Auction has been updated. Please refresh and try again").

This correctly handles the *first-bid* race too: when `CurrentHighBid` is null, the
predicate becomes `... IS NULL`; the first save flips it non-null, so the second racer
matches 0 rows and loses. The business checks (e.g. "bid too low") are only a fast-path
on a stale snapshot. The token is what actually enforces serialization.

---

## 2. Concurrent checkout creation for one auction: unique index + catch/re-read

**Mechanism:** unique index on `Payment.AuctionId`, enforcing one payment per auction.

In `PaymentService.CreateCheckoutSession`: it checks for an existing payment, and if none,
inserts one. Two simultaneous requests both see "no payment" and both try to insert.
Postgres rejects the loser with a unique violation, surfaced as `DbUpdateException`. The
`when (IsUniqueViolation(e))` filter (matching `PostgresErrorCodes.UniqueViolation`)
handles it by:

1. Detaching the failed `Added` entity (`PaymentRepository.Detach`) so the next
   `SaveChanges` won't retry the doomed insert.
2. Re-reading the row the winner committed, and continuing (or bailing out if it's
   already `Paid`).

Net result: both callers succeed, exactly one payment row exists. This is exactly what
`PaymentConcurrencyTests.ConcurrentCheckout_ForSameAuction_LeavesExactlyOnePaymentAndBothSucceed`
asserts.

The same shape guards the *attempt* insert one level down. A partial unique index allows one
`Pending` attempt per payment, so two callers racing past the payment insert still produce a single
open checkout session; the loser detaches its attempt and reuses the winner's via
`TryReuseOpenSessionAsync`.

---

## 3. Duplicate webhook completion: partial unique index + idempotent transition

Stripe can redeliver `checkout.session.completed`, and a payment can have multiple
attempts. Two guards prevent double-payment:

**a) Partial unique index** on `PaymentAttempt.PaymentId` filtered to `"Status" = 1`
(Completed). This lets a payment have many `Pending`/`Failed`/`Expired` attempts but
**at most one `Completed`** attempt. If a second attempt tries to complete, Postgres
rejects it; `HandleWebhook` swallows and logs it via `when (IsUniqueViolation(e))` rather
than propagating, verified by
`Webhook_SecondCompletedAttempt_IsRejectedByPartialIndexAndSwallowed`.

**b) Idempotent state transition** `Payment.MarkPaid`: early-returns if already `Paid`, so
webhook redelivery can't re-stamp `CompletedAt` or re-flip status. It's also the only
sanctioned way to set `Paid`.

**c) Unique index on `PaymentAttempt.StripeSessionId`** ties one attempt to one Stripe
session.

---

## 4. Duplicate Stripe-side sessions: idempotency key

When creating the Checkout Session, a fresh `IdempotencyKey = Guid.NewGuid().ToString()` is
passed. If the create call is retried at the transport level (a socket reset, a duplicated
request), Stripe returns the original session instead of creating a second one.

A per-call GUID rather than a key derived from a persisted row, because the session is created
*before* the attempt is inserted (§5) and at call time there is no attempt id to key on. The GUID
still protects the retry it needs to protect, since that retry is issued by the same in-flight
call. It deliberately does not deduplicate two *separate* requests: the partial unique index on
`Pending` attempts is what stops a second open session existing.

---

## 5. Known limitations

- **DB is the source of truth for races**, not in-memory locking. This is the right call
  for a horizontally-scalable API where multiple instances share one Postgres: in-process
  locks wouldn't help. The `PaymentConcurrencyTests` are explicitly gated behind an
  `Integration`/Docker trait because EF InMemory can't reproduce these unique-violation
  semantics.

- **Ordering across the Stripe boundary.** `CreateCheckoutSession` persists the payment, calls
  Stripe, and only then inserts the attempt carrying the returned session id. A wrapping
  transaction is *not* the fix here: the boundary is between a DB write and an external HTTP
  round-trip, and a local transaction cannot span the latter. This is the classic dual-write
  problem, and the ordering is the mitigation.

  The order is deliberate. A crash after the Stripe call but before the attempt insert leaves an
  **orphaned Stripe session with no DB record**, which self-expires via the `ExpiresAt` set at
  creation; its expiry webhook finds no attempt and no-ops. The reverse order is worse: it leaves a
  `Pending` attempt with a null `StripeSessionId`, which the reuse path cannot recover and the
  partial unique index on `Pending` blocks behind forever. Both orders leak something on a crash;
  this one leaks the thing that cleans itself up.

- **A dropped `checkout.session.completed` webhook is not reconciled.** Nothing sweeps `Pending`
  attempts and re-reads them from Stripe, so a webhook Stripe never successfully delivers leaves a
  genuinely paid attempt stuck `Pending`. `TryReuseOpenSessionAsync` recovers it opportunistically
  on the buyer's next checkout attempt (it reads the live session and promotes a `complete` one
  through `Payment.MarkPaid`), but nothing recovers it if the buyer never comes back. The recurring
  jobs are `auction-settlement`, `outbox-dispatch`, and `session-sweep`; there is no payment reaper.

- **`Payment.Status` itself has no concurrency token.** Protection against
  double-completion rests entirely on the partial index (3a) plus the `MarkPaid` guard (3b),
  not optimistic concurrency on the payment row. That combination is sufficient for the
  completed-attempt race, but there's no token guarding e.g. concurrent expiry vs.
  completion on the *same* attempt. In practice Stripe won't send both for one session, so
  it's a low-risk edge.

- **Rejected bids are not recorded anywhere.** `PlaceBid` returns on a failed guard without
  writing a row, so a bidder who keeps losing races leaves no trace. `Bids` is a log of accepted
  bids only. See [kafka.md](kafka.md) §1e, which is where this gap surfaced.

- **The bid race itself has no test.** Every other mechanism here is pinned (table below). §1 is
  not: `BidServiceTests` covers the business guards and the happy path, but nothing drives two
  connections at one auction row to assert the `DbUpdateConcurrencyException` branch. The mechanism
  is EF's own rather than hand-rolled, which is why it was deprioritised, not a reason to call it
  covered.

## Summary

| Race | Mechanism | Pinned by |
|---|---|---|
| Two bids on one auction | `[ConcurrencyCheck]` on `Auction.CurrentHighBid` | not covered, see above |
| Two checkouts for one auction | Unique index on `Payment.AuctionId`, catch and re-read | `PaymentConcurrencyTests.ConcurrentCheckout_ForSameAuction_...` |
| Two open sessions for one payment | Partial unique index on `Pending` attempts, reuse the winner's | `PaymentServiceTests.CreateCheckoutSession_WithOpenSession_...` |
| Redelivered completion webhook | Partial unique index on `Completed` attempts + idempotent `MarkPaid` | `PaymentConcurrencyTests.Webhook_SecondCompletedAttempt_...`, `PaymentWebhookTests` |
| Transport-level retry of a Stripe create | Per-call idempotency key | `PaymentServiceTests.CreateCheckoutSession_WhenWinner_...` |
