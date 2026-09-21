# Runbook: winner email, end to end

**Scope:** driving the full winner-email path by hand (settlement sweep → outbox → dispatch → rendered email)
**Companion docs:** [winner-email.md](winner-email.md), [claim-and-lease.md](claim-and-lease.md)

This runbook reproduces the winner-email flow by hand. It exercises every link in the chain:
the `AuctionSettlementJob` sweep finalizes an ended auction and enqueues an `AuctionEnded`
outbox row, the `OutboxDispatcher` claims it, and `AuctionEndedHandler` renders `Winner.cshtml`
and sends the email, caught locally by Mailpit so no real inbox or provider is needed.

For the automated payment-side tests, see [../TESTING.md](../TESTING.md).

---

## Prerequisites

- Docker (for Postgres + Mailpit via `compose.yaml`)
- .NET 10 SDK
- Optional: `dotnet dev-certs https --trust` so the browser doesn't warn on `https://localhost:5001`

---

## 1. Start infrastructure

```bash
cd <repo root>
docker compose up -d
```

This starts:

- **Postgres** on `localhost:5432` (db `auction`, user/pass `postgres`)
- **Mailpit**, SMTP catcher on `1025`, web UI on **http://localhost:8025**

The dev `Email` config in `API/appsettings.Development.json` already points at Mailpit
(`Host=localhost`, `Port=1025`), so sent mail lands in the web UI instead of a real inbox.

Wait for the DB to report healthy:

```bash
docker compose ps        # db STATUS should read "healthy"
```

## 2. Run the API

```bash
dotnet run --project API
```

Listens on **https://localhost:5001**. On startup it installs the Hangfire schema, applies EF
migrations, seeds data, and registers three recurring jobs (`auction-settlement`, `outbox-dispatch`,
`session-sweep`). Confirm a clean boot: look for `Application started` and `Starting Hangfire
Server` with no unhandled exception.

## 3. Create a test winner

The seeder creates two users, `alice@test.com` and `bob@test.com` (password `Pa$$w0rd`), and two
auctions that both end days from now, so neither is settleable. Insert one **ended, unfinalized**
auction with **bob** as the winner. The subqueries resolve the user IDs by email, so this survives
a reseed:

```bash
docker exec $(docker compose ps -q db) psql -U postgres -d auction -c "
INSERT INTO \"Auctions\"
  (\"ItemName\",\"StartingPrice\",\"SellerId\",\"StartTime\",\"EndTime\",\"CurrentHighBid\",\"CurrentHighBidderId\",\"FinalizedAt\")
VALUES (
  'Manual Test Item', 100,
  (SELECT \"Id\" FROM \"AspNetUsers\" WHERE \"Email\"='alice@test.com'),
  now() - interval '2 days', now() - interval '1 hour',
  1234.56,
  (SELECT \"Id\" FROM \"AspNetUsers\" WHERE \"Email\"='bob@test.com'),
  NULL)
RETURNING \"AuctionId\";"
```

The auction must have `EndTime` in the past, `FinalizedAt IS NULL`, and both `CurrentHighBid`
and `CurrentHighBidderId` set, otherwise the sweeper skips it (it only emails auctions that
have a winner).

## 4. Fire the jobs

Both jobs in this chain run every minute. Either wait 1-2 minutes, or trigger them instantly:

- Open the Hangfire dashboard at **https://localhost:5001/hangfire** → **Recurring Jobs**.
- Trigger `auction-settlement` → **Trigger now**, wait a couple of seconds, then trigger
  `outbox-dispatch` → **Trigger now**.

Order matters: the sweep writes the outbox row, the dispatch sends the email. In Development the
dashboard needs no login, because `HangfireDashboardAuthFilter` returns `true`. (In
non-Development it requires an authenticated user in the `Admin` role.)

## 5. Verify the email

Open Mailpit at **http://localhost:8025**. Expect a message:

- **To** bob@test.com · **From** Auction Site (dev) `<auctions@localhost>`
- **Subject** `You won "Manual Test Item"`
- **Body** with the GWAuction layout (dark shell, "Auction won" badge), amount rendered as
  `$1,234.56`, and a "Complete your payment" button pointing at
  `https://localhost:4200/auctions/{id}?pay=1`.

## 6. Verify the backend state

```bash
docker exec $(docker compose ps -q db) psql -U postgres -d auction -c "
SELECT \"Type\",\"Status\",\"Attempts\",\"LastError\" FROM \"OutboxMessages\";
SELECT \"AuctionId\",\"FinalizedAt\" FROM \"Auctions\" WHERE \"ItemName\"='Manual Test Item';"
```

Expected:

- Outbox row at **`Status = 1`** (Processed), `Attempts` low, `LastError` null.
- The auction with a non-null `FinalizedAt`.

### Reading a failure

`OutboxMessageStatus`: `0 = Pending`, `1 = Processed`, `2 = DeadLettered`.

- **`Status = 0` with a `LastError`**: a handler threw and the message is mid-retry; read
  `LastError` for the cause.
- **`Status = 2`**: dead-lettered after exhausting `Outbox:MaxAttempts` receives.

To re-run a message without recreating the auction, reset it and trigger `outbox-dispatch` again:

```bash
docker exec $(docker compose ps -q db) psql -U postgres -d auction -c "
UPDATE \"OutboxMessages\" SET \"Status\"=0,\"Attempts\"=0,\"VisibleAt\"=now(),\"LastError\"=NULL;"
```

## 7. Clean up

```bash
docker compose down        # stop containers, keep the pgdata volume
docker compose down -v      # also wipe the database volume for a fully fresh start
```

---

## Configuration reference

| Setting | Default | Effect |
| --- | --- | --- |
| `Settlement:SweepCron` | `Cron.Minutely()` | Cadence of the settlement sweep |
| `Outbox:DispatchCron` | `Cron.Minutely()` | Cadence of the outbox dispatcher |
| `Settlement:BatchSize` | `50` | Auctions finalized per sweep |
| `Outbox:BatchSize` | `20` | Messages claimed per dispatch |
| `Outbox:MaxAttempts` | `8` | Receives before a message is dead-lettered |
| `Outbox:LeaseMinutes` | `5` | How long a claimed message stays invisible to other workers |
| `Email:*` | Mailpit (dev) | SMTP host/port/from, see `appsettings.Development.json` |
