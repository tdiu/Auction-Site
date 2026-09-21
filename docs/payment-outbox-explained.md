# The payment outbox, explained

**Scope:** the outbox pattern from the ground up, written for someone new to it
**Companion docs:** [claim-and-lease.md](claim-and-lease.md) for the concurrency contract, [winner-email.md](winner-email.md) for the email handlers

A ground-up walkthrough of the payment outbox. It covers *why* the outbox exists, the three moving
parts (producer → handler → dispatcher), and the two ideas that trip people up: the **DI scope per
message** and the **co-commit**.

---

## 1. The problem: the dual-write

When a payment completes, Stripe calls our webhook. We want to do two things:

1. Mark the payment paid **in our database**.
2. Trigger a follow-up side effect (message the seller "payment received", and email the buyer a
   receipt).

The naive code is "do #1, then do #2." The trap: those are **two separate systems** (our DB and an
email provider). If the process crashes *between* them, we've marked the payment paid but the
receipt never goes out. Send the email first and then the DB write fails, and we've told someone
about a payment our database doesn't record. There is no way to make "write to Postgres" and "call
an email API" happen atomically: they are different machines. This is the **dual-write problem**.

The **outbox pattern** solves it with a move: instead of doing the side effect directly, we write
a *row describing the side effect* into the **same database**, in the **same transaction** as the
payment update. Now it is a single-database write, fully atomic, and it can't half-happen. Then a
**separate background job** reads those rows later and actually performs the side effect. The row
is a durable to-do note: "someone needs to send this receipt." If the process crashes, the note
survives, and the job picks it up next time.

So the outbox has two halves:

- **Producer**: writes the outbox row, transactionally, alongside the real state change.
- **Dispatcher**: a background job that reads outbox rows and runs the side effect.

The single sentence to carry: **the outbox trades "do the side effect now, hope nothing crashes"
for "record the intent atomically now, deliver it reliably later."**

---

## 2. The producer (inside the webhook)

`API/Services/PaymentService.cs`, the `checkout.session.completed` branch:

```csharp
if (attempt.Status != PaymentAttemptStatus.Completed)
{
    attempt.Status = PaymentAttemptStatus.Completed;
    attempt.CompletedAt = now;

    var auction = await unitOfWork.Auctions.GetAuctionAsync(attempt.Payment.AuctionId)
                  ?? throw new InvalidOperationException(...);

    var payload = JsonSerializer.Serialize(new PaymentCompletedPayload(...));

    // One payload, two rows: the seller's notification and the buyer's receipt retry
    // independently. §7.5 is the whole argument for why.
    unitOfWork.Outbox.Add(new OutboxMessage
    {
        Type = "PaymentCompleted", CreatedAt = now, VisibleAt = now, Payload = payload
    });
    unitOfWork.Outbox.Add(new OutboxMessage
    {
        Type = "PaymentReceipt", CreatedAt = now, VisibleAt = now, Payload = payload
    });
}
attempt.Payment.MarkPaid(now);

try { await unitOfWork.CompleteAsync(); }   // ONE SaveChanges commits payment + both outbox rows
catch (DbUpdateException e) when (e.IsUniqueViolation()) { ...log... }
```

Things to notice:

- **The outbox `Add`s and the `MarkPaid` are saved in one `CompleteAsync()`.** That is the whole
  point: the outbox rows and the payment update co-commit. Either all of them land or none do.
- **It sits inside the `if (Status != Completed)` guard.** Stripe delivers webhooks
  **at-least-once**, so it may call us twice for the same payment. The guard means the *second*
  delivery sees "already Completed," skips the block, and **adds no duplicate outbox row.**
  Idempotency at the producer.
- **`Payload` is JSON.** The row doesn't hold C# objects; it holds a serialized snapshot of
  everything the handler will need later (`PaymentId`, `AuctionId`, buyer, seller, item name). We
  snapshot rather than re-fetch so the handler acts on the facts as they were at payment time. The
  `PaymentCompletedPayload` record is shared by producer and handler so the JSON keys line up.
- **`VisibleAt = now`** means "this row is due immediately." `VisibleAt` is the cleverest column
  here; see §4.
- **The `catch` for unique violation** is a backstop: if two deliveries race, a unique index
  rejects the second, and its *entire* save (including its outbox row) rolls back.

---

## 3. The handler (the actual side effect)

`API/Services/Outbox/Handlers/PaymentCompletedHandler.cs`:

```csharp
public class PaymentCompletedHandler(IUnitOfWork unitOfWork) : IOutboxHandler
{
    public string Type => "PaymentCompleted";

    public Task Handle(OutboxMessage message, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<PaymentCompletedPayload>(message.Payload) ?? throw ...;

        unitOfWork.Messages.AddMessage(new Message
        {
            Id = $"payment-completed-{p.PaymentId}",   // deterministic id
            SenderId = p.BuyerId,
            RecipientId = p.SellerId,
            Content = $"Payment received for \"{p.ItemName}\".",
            MessageSent = DateTime.UtcNow
        });

        return Task.CompletedTask;   // dispatcher owns the save
    }
}
```

`IOutboxHandler` is a plug-in point: each message `Type` has one handler, matched by the `Type`
string. Two rules the interface docstring calls "load-bearing":

1. **Any DB write must use a deterministic id** derived from the payload, here
   `payment-completed-{PaymentId}`. If this handler runs twice, both produce the identical id, and
   the primary-key constraint rejects the duplicate. That is *why* a duplicate-key error can be
   read as success (§5).
2. **Do external I/O here, but only *stage* DB writes, never commit.** The handler returns
   `Task.CompletedTask` and never calls `SaveChanges`. The **dispatcher** owns the commit, so it
   can bundle the handler's DB writes together with marking the message done, in one transaction.

---

## 4. The dispatcher and the lease

The dispatcher (`API/Services/Outbox/OutboxDispatcher.cs`) is a recurring job, structurally like
the settlement sweeper, but with a problem the sweeper didn't have: **it makes external calls, so
it must NOT hold a database transaction open while it works.** Holding a lock while waiting on a
slow network call starves the connection pool. The sweeper could hold its transaction because
everything it did was local; the dispatcher can't.

So instead of a held row lock, it uses a **lease**, via the `VisibleAt` column (borrowed from AWS
SQS visibility timeouts). `VisibleAt` does two jobs:

- **Claiming** a row pushes `VisibleAt` into the future by the lease duration (5 min). While
  `VisibleAt` is in the future, no other dispatcher picks the row up: the claim query only selects
  rows where `VisibleAt <= now`. The row is reserved for us **without holding a DB lock.**
- **Failing** to process a row also pushes `VisibleAt` into the future (by a **backoff** delay),
  so a failing message waits before being retried.

One column, both behaviors. The claim runs in a single atomic SQL statement
(`OutboxRepository.ClaimAndLeaseAsync`), so it needs no long-lived transaction, which is exactly
what frees the dispatcher to do slow external I/O with nothing locked. (Full concurrency contract:
[`claim-and-lease.md`](claim-and-lease.md).)

The loop:

```csharp
[DisableConcurrentExecution(timeoutInSeconds: 55)]
public async Task DispatchAsync(CancellationToken ct)
{
    var reaped = await outboxRepository.ReapExhaustedAsync(maxAttempts);   // retire dead rows
    if (reaped > 0) logger.LogError(...);

    var ids = await outboxRepository.ClaimAndLeaseAsync(batchSize, lease, maxAttempts);  // reserve

    foreach (var id in ids)
    {
        if (ct.IsCancellationRequested) break;   // shutting down? unworked leases just lapse
        await ProcessAsync(id, maxAttempts, ct);
    }
}
```

`[DisableConcurrentExecution]` here is **not** load-bearing: the lease already makes concurrent
runs safe. It only stops slow runs stacking up.

---

## 5. Why a unique violation means *success*

This is the counterintuitive part: the dispatcher catches a duplicate-key error and treats it as
"delivered successfully."

The scenario: the lease is *best-effort*, not a hard lock. Dispatcher A claims a message, gets a
5-minute lease, starts the handler, then A stalls (GC pause, hung pod). The lease expires.
Dispatcher B now sees the row as due, claims it, runs the handler too. Both stage a `Message` with
the same deterministic id `payment-completed-42`. That id is the primary key. Whichever commits
second gets a `23505` unique-violation.

What does that error *mean*? It means **the message was already delivered**: the other
dispatcher's `Message` already landed. The side effect happened. That is the idempotency mechanism
*working correctly*. So the right response is: mark this row `Processed` and move on.

Treating it as a failure would poison the row: the winning `Message` is permanently in the DB, so
every retry re-collides, the row burns through its attempts, and it **dead-letters with
`LastError = duplicate key`**, a false alarm raised by the idempotency mechanism succeeding. Hence
the `catch (DbUpdateException e) when (e.IsUniqueViolation())` arm, which calls
`MarkProcessedAsync`.

Everything else (a real handler failure) falls to `catch (Exception ex) => RecordFailureAsync`,
which records `LastError`, and either **backs off** (`VisibleAt = now + Backoff(Attempts)`, waits
of 2/4/8/16… minutes) or, once `Attempts >= maxAttempts`, **dead-letters** the row (`Status =
DeadLettered`, logged at error level for a human to alert on). `Attempts` is counted at *claim*
time, not on failure: a process that dies mid-handler never reaches a catch block, so counting
receives (as SQS does) is the only thing that gives a crash-looping message a ceiling.

---

## 6. The DI scope per message (the part everyone trips on)

`OutboxDispatcher.ProcessAsync` opens a fresh scope for **each message**:

```csharp
private async Task ProcessAsync(Guid id, int maxAttempts, CancellationToken ct)
{
    using var scope = scopeFactory.CreateScope();       // new scope
    var sp = scope.ServiceProvider;
    var unitOfWork = sp.GetRequiredService<IUnitOfWork>();   // fresh UoW + fresh DbContext
    ...
}
```

To understand why, you need three facts.

### 6.1 The three DI lifetimes

- **Singleton**: one instance for the whole app.
- **Scoped**: one instance *per scope*. In a normal web request, ASP.NET creates a scope at the
  start of the request and disposes it at the end. So "scoped" effectively means "one per HTTP
  request."
- **Transient**: a brand-new instance every time it's asked for.

Our `AppDbContext` is registered **scoped** (that's what `AddDbContext` does by default), and so
are `UnitOfWork` and the repositories. So *within one scope*, every service that asks for an
`AppDbContext` gets the **same instance**. That is the entire reason the unit-of-work pattern
works: `UnitOfWork`, `PaymentRepository`, `MessageRepository`, `OutboxRepository` all share one
`DbContext`, so one `SaveChanges()` commits all their staged changes together.

### 6.2 A `DbContext` is a short-lived, stateful notepad

A `DbContext` has a **change tracker**, an in-memory list of every entity it has loaded or been
told to add, and what's dirty. Two consequences:

1. It is **not thread-safe** and is meant to be used for one unit of work then discarded.
2. Once a `SaveChanges()` **throws**, the context is **poisoned**: the change tracker still holds
   the entities that failed, so the *next* `SaveChanges()` on that same context re-tries them. You
   cannot trust a context after it has thrown.

### 6.3 Why a background job needs `CreateScope()`

A background job is **not** an HTTP request, so nothing creates a per-request scope for it. If the
dispatcher reused one `DbContext` for every message in the batch, you'd get two failures:

- **Contamination.** If message 2's handler stages a `Message` then throws before commit, that
  `Message` is still in the shared change tracker. Moving to message 3 and calling `SaveChanges()`
  would flush *everything pending*, including message 2's orphaned write.
- **Poisoned context.** After message 2's commit throws, the context is undefined; using it for
  message 3 is a bug.

`scopeFactory.CreateScope()` creates a fresh scope per **message**, the same thing ASP.NET does
per request, but deliberate. Inside it, `GetRequiredService<IUnitOfWork>()` builds a `UnitOfWork`
backed by a **brand-new `AppDbContext`** with an empty change tracker. The `using` disposes the
whole scope when the message is done. If a handler throws mid-write, that scope's `DbContext` is
**discarded without ever calling `SaveChanges`**, so the partial write physically cannot reach the
database. That is what "structural, rather than a rule handlers are asked to follow" means: the
architecture makes a handler's mess un-committable by construction. `MarkProcessedAsync` and
`RecordFailureAsync` each open their *own* fresh scope for the same reason, because the caller's
context is poisoned by the write that just failed.

---

## 7. The co-commit

The dispatcher resolves the handler from `sp` (**this message's scope**) rather than from its own
constructor:

```csharp
var handler = sp.GetServices<IOutboxHandler>().FirstOrDefault(h => h.Type == message.Type) ?? throw ...;
await handler.Handle(message, ct);      // stages a Message into the scope's DbContext

message.Status = OutboxMessageStatus.Processed;   // updates a tracked entity in the SAME context
message.ProcessedAt = DateTimeOffset.UtcNow;
await unitOfWork.CompleteAsync();       // ONE SaveChanges → ONE transaction → both commit
```

The key insight: **`AddMessage(...)` and `message.Status = ...` do not touch the database.** They
each just make a note in the `DbContext`'s change tracker. Nothing is sent to Postgres until
`SaveChanges` runs.

### 7.1 The notepad fills up, then flushes once

```csharp
unitOfWork.Messages.AddMessage(new Message { Id = "payment-completed-42", ... });
```

No `INSERT` is sent. The context records: *"pending: INSERT this Message on save."* Then:

```csharp
message.Status = OutboxMessageStatus.Processed;
```

`message` is **tracked** (it was loaded via `unitOfWork.Outbox.GetAsync(id)` into this context), so
EF notices the change and records: *"pending: UPDATE this OutboxMessage's Status on save."* Now the
tracker holds two pending operations:

```
DbContext (change tracker)
 ├─ INSERT Message  "payment-completed-42"     ← staged by the handler
 └─ UPDATE OutboxMessage 0e36… SET Status=1    ← staged by the dispatcher
```

Still nothing in the DB. Then `CompleteAsync()` → `SaveChangesAsync()` flushes **everything at
once**, and EF wraps a multi-statement `SaveChanges` in a **single database transaction**. Both the
INSERT and the UPDATE hit Postgres inside one `BEGIN … COMMIT`. Either both land or both roll back.
**That atomicity is the co-commit**, and it happens for free, precisely *because* both notes are
on the same notepad.

### 7.2 Why "same `DbContext`" is the load-bearing condition

Both notes only land on the *same* notepad if the handler and the dispatcher use the **same
`DbContext` instance**. They do, because both are resolved from the same scope, and both
`UnitOfWork` and `AppDbContext` are **scoped**:

- The dispatcher's `unitOfWork` (from `sp.GetRequiredService<IUnitOfWork>()`) wraps context **X**.
  `message` was loaded through it → tracked by **X**.
- The handler `PaymentCompletedHandler(IUnitOfWork unitOfWork)` is resolved from the **same `sp`**.
  Asking the same scope for `IUnitOfWork` again returns the **same instance**, wrapping the **same
  context X**. Its `AddMessage(...)` note lands on **X** too.

Both notes on X → one `SaveChanges` on X → one transaction → co-commit.

### 7.3 The counterfactual

If the handler were injected into the **dispatcher's constructor** (an outer scope), it would carry
a *different* context, **Y**:

```
Handler's context Y:            Dispatcher's context X:
 └─ INSERT Message "…-42"        └─ UPDATE OutboxMessage SET Status=1

dispatcher calls X.SaveChanges()  →  commits the UPDATE only.
                                     Y is never saved. Message "…-42" vanishes.
```

You'd flip the row to `Processed` ("delivered, done") while the actual side effect was staged on
Y and silently dropped when Y was disposed unsaved. **Delivered-but-didn't-happen**, and it fails
*silently*. That is exactly why the dispatcher injects `IServiceScopeFactory` and pulls the handler
from `sp` instead of taking handlers in its constructor.

### 7.4 Why this is "exactly-once for in-app side effects"

Because the `Message` insert and the `Status = Processed` update commit together:

- Transaction **commits** → the side effect happened *and* the row is marked done. Won't be
  re-processed. Once.
- Transaction **rolls back** → *neither* happened. The row stays `Pending`, gets re-claimed, and
  the handler runs again. Still ends at once.

There is no window where the side effect happened but the row looks unprocessed, or vice versa.

**The fine print:** this only holds because the side effect here *is itself a database write in the
same DB* (a `Message` row). The moment a handler's side effect goes **external**, as an email does,
you can't co-commit "email sent" with "row processed" (the email provider isn't in your
transaction), so that handler drops to **at-least-once** and leans on the provider's idempotency
key plus the deterministic id instead. Losing the co-commit at the system boundary is the whole
reason the outbox pattern exists.

### 7.5 Why one handler must not own two kinds of side effect

A completed payment owes two things: an in-app notification to the seller (a DB write) and a
receipt email to the buyer (an external send). Putting both in one handler is the obvious shape and
it is wrong, in two ways that are worth spelling out because neither is visible from reading the
handler.

**The 23505 rule cannot cover the send.** A combined handler would stage the DB write (no SQL yet),
send the email, and only then let the dispatcher commit. On a redelivery the email goes out
*before* the duplicate key is detectable. The guard in §5 would protect the `Message` perfectly and
the receipt not at all, which is a confusing thing to have to explain about a single handler.

**One retry budget for two effects.** This is the one that actually bites. If the send throws, the
exception propagates, the scope is discarded unsaved (§6.3), and the staged `Message` never lands.
Retry, same thing. After `maxAttempts` the row dead-letters and **the seller never gets their
notification**, despite that write having been guaranteed to succeed on every single attempt. A mail
misconfiguration would present as a notifications outage, with `LastError` pointing at SMTP.

So the payment writes **two** rows in the one producer commit, `PaymentCompleted` and
`PaymentReceipt`, each with its own handler, `Attempts`, backoff and dead-letter verdict:

| Row | Side effect | Guarantee | Idempotency |
|---|---|---|---|
| `PaymentCompleted` | seller's in-app `Message` | exactly-once | deterministic `Message.Id` + 23505-as-delivery |
| `PaymentReceipt` | buyer's receipt email | at-least-once | `payment-receipt-{PaymentId}` key, which nothing honours today |

`AuctionEndedHandler` is the pure external case, with no DB write at all.
`OutboxDispatcherTests.A_failing_receipt_row_does_not_hold_back_the_notification_row` asserts the
split does what it claims. This is the same argument [kafka.md](kafka.md) §2 makes for
per-consumer-group offsets, applied within one service instead of across a broker: one retry state
per side effect, not per event. What the receipt's key does and does not buy is
[winner-email.md](winner-email.md) §4.

---

## Summary

| Layer | Job | Idempotency defence |
|-------|-----|---------------------|
| Producer (webhook) | Write both outbox rows in the same tx as `MarkPaid` | `if (Status != Completed)` guard + unique index |
| Handler | One kind of side effect each, *staging* DB writes only | Deterministic `Message.Id`, or a deterministic key on a send |
| Dispatcher | Lease a batch, run each in its own scope, commit result | Lease + `23505`-means-success + backoff/dead-letter |

Every layer assumes at-least-once execution and defends with idempotency. The two subtle
mechanisms, the **scope per message** (isolation by construction) and the **co-commit** (side
effect and its record commit as one), are what turn "run it twice" from a bug into a no-op.
