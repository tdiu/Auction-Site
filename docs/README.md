# Design notes

One note per problem, written while building the feature it describes. Every note documents code
that is on `main`; where a note argues against something, it says so in its own opening line. If a
note and the code disagree, the code is right and the note is the bug.

## How it works

| Doc | What it covers |
|---|---|
| [concurrency.md](concurrency.md) | The races in the bid, payment and signup paths, and the database-layer mechanism guarding each. Start here. |
| [payment-outbox-explained.md](payment-outbox-explained.md) | The transactional outbox from the ground up: producer, handler, dispatcher, and the two ideas that trip people up (DI scope per message, co-commit). |
| [claim-and-lease.md](claim-and-lease.md) | How the dispatcher hands work to competing workers without delivering twice, and what a lease trades away versus a lock. |
| [winner-email.md](winner-email.md) | The two transactional emails and the provider-agnostic layer behind them: seam, Razor templates, and the limits of idempotency over SMTP. |

## Decision records

| Doc | What it covers |
|---|---|
| [kafka.md](kafka.md) | Whether to put Kafka on the bid path. Decision: no, and the arguments that sound decisive mostly aren't. Records where it *would* legitimately fit. |

## Runbooks

| Doc | What it covers |
|---|---|
| [winner-email-testing.md](winner-email-testing.md) | Driving the full settlement → outbox → dispatch → email chain by hand against Postgres and Mailpit. |
| [../TESTING.md](../TESTING.md) | The three layers of payment test coverage, including the manual Stripe test-mode walkthrough. |
