# The outbox claim-and-lease pattern

**Scope:** how the outbox dispatcher hands work to competing workers without delivering twice
**Companion docs:** [payment-outbox-explained.md](payment-outbox-explained.md) for the pattern from the ground up, [concurrency.md](concurrency.md) for the rest of the system's races

This note explains how `OutboxDispatcher` hands out work to multiple competing workers without
delivering the same message twice, and what it trades away to do it.

![End-to-end outbox flow: two producers write an OutboxMessage in the same transaction as their
state change, the dispatcher reaps exhausted rows, claims a batch with FOR UPDATE SKIP LOCKED under
a lease, and runs each message in its own DI scope before one commit writes the handler's changes
plus Status = Processed.](Outbox.png)

*The design end-to-end; this note covers everything from the `OutboxMessages` table down. Two
details have moved on since the diagram was drawn: the payment producer now writes two rows rather
than one (`PaymentCompleted` and `PaymentReceipt`, see
[payment-outbox-explained.md](payment-outbox-explained.md) §7.5), and the email seam is implemented
by MailKit over SMTP rather than SendGrid.*

---

## 1. The problem

There is one `OutboxMessages` table and potentially several copies of the API running, which is
the point of putting Hangfire on the shared Postgres. Every copy runs the dispatcher on its
own ~1 minute schedule. Each message must be delivered *once*, and the workers cannot
coordinate through memory: they are separate processes, possibly separate machines. The
database is the only thing they share, so the database has to be what stops them colliding.

The naive version fails immediately:

```sql
SELECT * FROM "OutboxMessages" WHERE "Status" = 0;   -- worker A and worker B
```

Both see the same rows, both deliver, the buyer gets two emails.

---

## 2. Why not just hold a lock

The obvious fix is `SELECT ... FOR UPDATE` inside a transaction. It genuinely works, since
Postgres will not let two transactions lock the same row, but the locks only live as long as the
transaction, so you must **keep the transaction open the whole time you are working**.

The work here is an SMTP round trip to a mail relay. That would mean holding a database connection
and row locks across a network call to someone else's server that may take thirty seconds or hang
outright, for every message in the batch.

---

## 3. The lease idea

Instead of *holding* a lock, **write down** that the row is taken, and until when. That is
`VisibleAt`. It is borrowed from SQS's visibility timeout: claiming a message pushes
`VisibleAt` into the future, which makes the row invisible to everyone else's "what's due?"
query. Nothing is held open.

> **The core trick:** a lock is state held by a connection; a lease is state stored in a row.
> Only the second one survives you walking away to do slow work.

---

## 4. The claim, clause by clause

From `API/Data/OutboxRepository.cs`, inside out:

```sql
SELECT "Id" FROM "OutboxMessages"
WHERE "Status" = 0                    -- only Pending; Processed/DeadLettered are finished
  AND "VisibleAt" <= now()            -- "due"
  AND "Attempts" < {maxAttempts}      -- hasn't burned its budget
ORDER BY "VisibleAt", "CreatedAt"     -- oldest first (and matches the partial index)
LIMIT {batchSize}                     -- bounded work per tick
FOR UPDATE SKIP LOCKED                -- the concurrency primitive
```

`VisibleAt <= now()` collapses three different situations into one condition: a brand-new
message (the producer sets `VisibleAt = now`), a message whose lease expired because a worker
died, and a message that failed and is waiting out its backoff. All of them are just "due" or
"not due yet". One column, three jobs.

`FOR UPDATE SKIP LOCKED` is the part worth internalising. `FOR UPDATE` alone means worker B
*waits* for worker A. `SKIP LOCKED` means B says "that one's busy, give me a different one"
and takes the next rows instead. That is what turns a queue into something several workers can
drain in parallel rather than single-file.

The outer statement stamps the lease and hands back what you won:

```sql
UPDATE "OutboxMessages"
SET "VisibleAt" = now() + make_interval(secs => {leaseSeconds}),  -- taken for 5 min
    "Attempts"  = "Attempts" + 1
WHERE "Id" IN ( ...that select... )
RETURNING "Id" AS "Value"
```

The partial index in `AppDbContext.OnModelCreating`, `(VisibleAt, CreatedAt)` filtered on
`"Status" = 0`, exists to match this query's `WHERE`/`ORDER BY` exactly. Processed rows fall
out of the index, so it stays small no matter how large the table grows.

---

## 5. Why it is one statement

A single statement in Postgres is atomic on its own: it runs in an implicit transaction, so no
`BeginTransaction` is needed. The `FOR UPDATE` locks exist only for the milliseconds the
statement runs, and commit releases them. After that, the **lease** is what keeps the row
private.

The sequence is: grab locks for ~2ms → write "mine until 12:05" → release locks → go make a
30-second HTTP call with nothing held open.

This is why `IOutboxRepository` documents `ClaimAndLeaseAsync` as needing no ambient
transaction, and why `AuctionRepository.ClaimEndedUnfinalizedAsync` is its exact opposite.
That one is a bare `SELECT ... FOR UPDATE`, so its locks vanish at statement end and it is only
exclusive *inside* a transaction. Two claim methods, opposite requirements, near-identical
signatures. That difference is invisible at the call site, which is why the sweeper's claim
throws when no transaction is open rather than silently excluding nobody.

---

## 6. Why `Attempts` goes up at claim time

The intuitive place to count failures is the catch block. Consider a worker that is OOM-killed
mid-handler: **no catch block ever runs**. The lease expires, the row goes due again, another
worker picks it up, dies the same way, forever. Nothing ever counts, and the message crash-loops
in perpetuity.

Counting at *claim* time means every pickup costs one attempt whether or not you survive to
report anything. SQS calls this the receive count. It is what gives a crash-looping message a
ceiling.

---

## 7. The bill for using a lease

A lease is best-effort where a lock was absolute. If a handler outlives its lease (a long GC
pause, a stalled pod) the row becomes due again and a second worker legitimately claims it
*while the first is still running*. Both deliver.

This is not a bug that can be eliminated; it is the price of not holding a transaction open
across external I/O. So it is paid for elsewhere:

- **Handlers write with a deterministic id** derived from the payload (e.g.
  `payment-completed-{PaymentId}`), which is the `Message` primary key. The second delivery
  therefore collides rather than duplicating.
- **The dispatcher must read that `23505` as proof of prior delivery**, as success, not as an
  error.

That second rule is load-bearing and easy to get backwards. Treat the collision as a failure and
the row **poisons itself**: the first worker's `Message` is now permanently in the database, so
every retry re-collides, and the row eventually dead-letters with `LastError = duplicate key`,
a false alarm raised by the idempotency mechanism working correctly. A transient scheduling
hiccup would ratchet into a permanent fault.

The rule only covers handlers whose side effect **is** a database write, which is
`PaymentCompletedHandler` alone. The two email handlers send before the dispatcher commits, so
there is no key to collide on and a doubled delivery is a doubled email. See
[winner-email.md](winner-email.md) §4 for what is and is not done about that.

---

## 8. The reaper's role

The claim filters `Attempts < maxAttempts`, which *stops* claiming an exhausted row but never
*retires* it. Dead-lettering lives in the dispatcher's catch block, and the crash-loop case
never reaches one. Without intervention the row sits `Pending` forever: undelivered,
un-dead-lettered, and invisible to any alert on `Status = 2`.

`ReapExhaustedAsync` exists purely to sweep up that specific corpse.

---

## Summary

This is SQS, rebuilt in a table we already have: claim, lease, work outside the lock, count
receives, dead-letter the hopeless. The delivery guarantee is **at-least-once**, and it is paid
for with idempotent handlers.

See also [concurrency.md](concurrency.md) for how the rest of the system handles races.
