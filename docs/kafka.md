# Kafka: where it fits (and where it doesn't)

**Decision record.** There is no Kafka in this codebase, by choice. This note records the
evaluation, because the arguments that *sound* decisive here mostly aren't: the real ones are
narrower than expected.

**Scope:** whether to put Kafka on the bid path, and where it would legitimately fit
**Companion docs:** [concurrency.md](concurrency.md) §1 for the mechanism that does the job instead, [payment-outbox-explained.md](payment-outbox-explained.md) for the outbox it would ride on

The proposal under evaluation: a Kafka queue for bids, partitioned by `auctionId`, to (a) serialize
concurrent bids by ordering, (b) provide an audit trail of failed bids, and (c) allow
multiple `BidService` instances to absorb bid volume.

---

## 1. Kafka before the database: rejected

### 1a. Bids need a synchronous authoritative answer

This is the argument that actually kills it. `BidService.PlaceBid` returns
accept-or-reject to the caller, who is a human staring at a button. Kafka has no
request-response: a producer learns its record was *durably appended*, never that the bid
*won*. Recovering an answer means the client polls, or subscribes to a reply topic
correlated by id, and now every bid pays a round-trip through a log to learn something
Postgres would have told it in one statement.

Everything else below is secondary to this.

### 1b. Kafka-first splits one atomic commit into an eventually-consistent pipeline

Today `PlaceBid` is one transaction: read auction → validate → mutate `CurrentHighBid` →
`CompleteAsync()`. Publishing to Kafka first and consuming into Postgres afterward is a
**dual write pointing the other way**, the same failure mode the outbox exists to
prevent (see [payment-outbox-explained.md](payment-outbox-explained.md) §1).
Accepted-in-Kafka and rejected-by-the-DB become two different truths, and the
reconciliation is yours to write.

### 1c. Validation needs authoritative current state

"Bid too low" is only answerable against the committed `CurrentHighBid`. A producer
doesn't have it; it has a snapshot from whenever it last read. The consumer would have
to re-validate anyway, so the producer-side check becomes decorative and the log fills
with bids that were never going to land.

### 1d. The contention is real, but Postgres already solves it in two lines

The ordering concern is legitimate: concurrent bids on one auction *do* race. But the
fix isn't a partitioned log, it's `SELECT ... FOR UPDATE` on the auction row (or the
`[ConcurrencyCheck]` on `CurrentHighBid` we already have, see
[concurrency.md](concurrency.md) §1). Postgres serializes writers to a single row without
any new infrastructure. Kafka's per-partition ordering buys the same guarantee at the cost
of a broker, a consumer group, and an async boundary.

An auction site's bid volume also isn't near Postgres's ceiling. Load-balancing and
buffering are solving a problem we don't have.

### 1e. Failed bids never reach Kafka, so it can't be the audit trail

In a DB-first design, a rejected bid fails inside `PlaceBid` and returns. It's never
published. Kafka only ever sees successes, which makes it exactly the wrong place to look
for unexpected failures. Rejected bids genuinely have no audit trail today, and that is a
limitation of the bid path rather than of the transport: closing it means writing rejections from
inside `PlaceBid`, which is equally available with or without a broker. Kafka was never going to
close it. (Also recorded in [concurrency.md](concurrency.md) §5.)

### What is *not* an argument here

Two things came up that don't apply:

- **Fan-out.** There's one consumer of a bid (the DB write). Fan-out is a downstream
  concern, not a bid-path one.
- **Scale.** See 1d, not a bid-path argument either, at this volume.

---

## 2. Kafka after the database: coherent, but not needed

The defensible shape is: `PlaceBid` commits to Postgres as it does today, and the commit
*emits* an event that lands in Kafka. Consumers react.

This works, and it's where the outbox and Kafka meet:

> **Kafka is a destination, not a replacement.** Introducing it makes the outbox *more*
> necessary, not less. "Commit the bid, then publish to Kafka" is a dual write across a
> network boundary, precisely what the outbox pattern exists to prevent. The outbox row
> commits with the bid; the dispatcher publishes to Kafka afterward. Kafka slots in as a
> new `IOutboxHandler` implementation.

Where Kafka genuinely beats the outbox table downstream: **independent per-consumer-group
offsets.** A shared outbox row has one retry state, so one flaky handler re-runs its
siblings and a dead-letter kills every side effect on that event. Kafka gives each
consumer group its own offset and its own failure domain. That's a real advantage, not a
one-word diff.

### Why it's still unnecessary

**The `Bids` table is already the log.** It's append-only, immutable, never deleted, and
`BidId` is a monotone identity int. Ordering "dissolves" as a problem: consumers order by
`BidId`, i.e. **by data, not by transport**. `BidId` is monotone *per auction* precisely
because `[ConcurrencyCheck]` serializes the winners. The ordering guarantee Kafka was
supposed to provide is a property we already have, sitting in a column.

And the fan-out has no members. Nothing downstream consumes a bid at all: SignalR carries presence
only, and pushing live bids into the browser is deliberately not a feature. A broker whose value is
fan-out, fanning out to nobody, is infrastructure with no consumer.

---

## 3. Partitioning by `bidId` vs `auctionId`

The original sketch partitioned by `bidId`. That's wrong on its own terms: partitioning by
a key that's unique per record gives every record its own partition, which is the same as
no ordering at all. `auctionId` is the correct key: it's the entity whose state is
contended.

The underlying error (**partitioning by the event rather than the entity**) recurs below.

---

## 4. Orleans, for contrast

Orleans came up as an alternative. The distinction:

| | Kafka | Orleans |
|---|---|---|
| Serializes by | **queueing**: async hand-off through a log | **addressing**: sync RPC to a single activation |
| Unit | partition | grain |
| Caller gets | durability ack | a return value |

Orleans would make each **auction** a grain: one activation, single-writer by
construction, and `PlaceBid` stays request-response. A **bid is not a grain**: it's an
event, not an addressable entity with state. That's the same error class as the `bidId`
partition key.

Both tools enforce single-writer. Kafka does it by making the write async; Orleans does it
while keeping the call synchronous, which is why Orleans is at least *shaped* like the bid
path in a way Kafka isn't.

---

## 5. Does eBay use Kafka for bids?

Partially answerable. eBay's **Rheos** is a documented Kafka-based streaming platform
handling ~100B messages/day. That's real, and it's for streaming and insights.

Whether Kafka sits in eBay's **bid write path** is *not* verifiable. The articles that
claim to describe it are interview-prep hypotheticals, not engineering-blog sources. It's
also deducible from the UX that it doesn't: eBay tells you immediately whether you're the
high bidder, and nobody decides that in a consumer.

---

## Verdict

| Component | Assessment |
|---|---|
| Outbox | **load-bearing** |
| `[ConcurrencyCheck]` on `CurrentHighBid` | **load-bearing** |
| Settlement sweeper | **load-bearing** |
| Hangfire | in the build, but a `BackgroundService` would do; chosen for the experience of it |
| Kafka | not built: the `Bids` table is already the log |

The honest framing is that Kafka here would be a **learning exercise for scaling systems**, in the
same category as Hangfire, which *is* in the build for exactly that reason. Wanting the experience
is a legitimate reason to build something; it just shouldn't be mistaken for a requirement, and it
shouldn't go on the bid path.

The only defensible shape, were it ever added, is: **downstream of the commit, delivered by the
outbox dispatcher as an `IOutboxHandler`, partitioned by `auctionId`.**
