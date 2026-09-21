# AuctionSite

A timed auction platform: sellers list items, bidders compete against a clock, and when the
clock runs out the system settles the auction, charges the winner, and notifies both sides.
Exactly once, even if something crashes mid-way.

The bidding is the easy part. The interesting problems are all at the edges: two settlement
workers waking on the same expired auction, a Stripe webhook arriving twice, a payment that
succeeds while the email announcing it fails.

**Stack:** ASP.NET Core (.NET 10), Angular 21, PostgreSQL 16, SignalR, Hangfire, Stripe, MailKit.

## Design notes

One per problem, written while building, indexed in [`docs/`](docs/README.md). They document code
that is on `main`; where one argues against something instead, it says so in its opening line.

* [Concurrency](docs/concurrency.md), the bidding and payment races and the tests that hold
  them down. The best one to read first.
* [The payment outbox](docs/payment-outbox-explained.md), why charging and notifying cannot
  share a transaction, and what does instead.
* [Settlement: claim and lease](docs/claim-and-lease.md), how an expiring auction gets settled
  once when several workers race for it.
* [Federated login](docs/federated-login.md), Google sign-in, one account per email, and
  generating a username that survives a uniqueness race.
* [Kafka](docs/kafka.md), a design that was proposed, evaluated, and turned down.

## What is built

**Auctions and bidding.** Timed listings, bid validation against the current high bid, and Buy Now,
which clamps the auction's end time to the moment of the bid.

**Settlement.** A Hangfire job claims expiring auctions under a lease, picks the winner, and
emits domain events. Safe to run with more than one worker: the lease is what stops two of them
settling the same auction twice.

**Payments.** Stripe checkout for the winner, with an order confirmation and order history.
Idempotent by construction: a replayed webhook or a double-submitted checkout cannot produce a
second charge.

**Transactional outbox.** Payment and settlement events are written in the same transaction as
the state change they describe, then dispatched to handlers separately. An email that fails to
send can never roll back a charge that succeeded, and the two side effects a completed payment
owes retry independently of each other.

**Transactional email.** Winner and receipt emails, rendered from Razor templates through a
provider-agnostic seam and delivered by the outbox. In development they land in a local SMTP
catcher, so a fresh clone can watch the whole chain run without an account or a key.

**Messaging.** Buyer and seller messaging with per-thread history, unread counts, and a live
online/offline indicator driven by a SignalR presence hub.

**Accounts.** ASP.NET Core Identity, short-lived JWT access tokens, and per-device refresh
sessions with rotation and reuse detection. This layer is deliberately deeper than a project
this size needs. I wanted to understand session security properly rather than accept a default,
so signing in on a phone does not sign you out on a laptop, and a replayed refresh token is
detected and cascades a revocation rather than silently working.

**Federated login.** Google sign-in, with one email mapping to exactly one account and one auth
method. No account linking in either direction, which is a deliberate limit rather than an
omission: linking a password account to a provider means deciding what happens when the provider
changes the email under you, and refusing is the honest answer at this size.

**Brute-force defence, in two layers that catch different attacks.** Per-IP and per-user request
quotas reject volume at the edge of the app, and a per-account failure counter locks an account
for 15 minutes after five bad passwords. The second layer exists because the first cannot see
credential stuffing spread thinly across many addresses, and the first exists because the second
cannot see one attacker sweeping many accounts.

## Running it

### Prerequisites

.NET 10 SDK, Node 22 or newer, Docker.

### 1. Start Postgres and the dev mail server

```bash
docker compose up -d
```

This brings up PostgreSQL on 5432 and [Mailpit](https://mailpit.axllent.org/) as a local SMTP
sink. Mail sent by the app is caught rather than delivered; read it at http://localhost:8025.

### 2. Configure the API

`API/appsettings.json` and `API/appsettings.Development.json` are both gitignored. Create the
Development one:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=auction;Username=postgres;Password=postgres"
  },
  "TokenKey": "replace-with-a-long-random-string-of-at-least-64-characters",
  "RefreshTokenKey": "replace-with-a-different-long-random-string",
  "ClientAppUrl": "https://localhost:4200",
  "Email": {
    "Host": "localhost",
    "Port": 1025,
    "UseStartTls": false,
    "FromAddress": "auctions@localhost",
    "FromName": "Auction Site (dev)"
  }
}
```

`TokenKey` must be at least 64 characters or the API throws when it first issues a token.

Secrets go in .NET user secrets rather than the file; the project is already set up for it:

```bash
cd API
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..."   # from `stripe listen`, see TESTING.md
dotnet user-secrets set "Google:ClientId" "....apps.googleusercontent.com"
dotnet user-secrets set "Google:ClientSecret" "..."
```

A Stripe **test-mode** key is required: the API resolves `Stripe:SecretKey` at startup and will not
boot without one. Google's pair is optional. Without it the provider is simply not registered and
the Google button 404s, leaving password sign-in working; with it, add
`https://localhost:5001/api/signin-google` as an authorised redirect URI in the Google Cloud
console.

Background job schedules (`Outbox:DispatchCron`, `Settlement:SweepCron`, `Session:SweepCron`) have
defaults and only need setting to change them. So do the outbox batch, lease and attempt limits;
[docs/winner-email-testing.md](docs/winner-email-testing.md) lists them.

#### Rate limiting behind a proxy

Rate limits partition anonymous callers by client IP. Nothing extra is needed locally, but once
the API sits behind a reverse proxy or CDN, `Connection.RemoteIpAddress` becomes the proxy's
address and every anonymous caller lands in one shared bucket. Declare the proxies so the
forwarded headers are honoured:

```json
"ForwardedHeaders": {
  "KnownProxies": ["10.0.0.5"],
  "KnownNetworks": ["173.245.48.0/20"]
}
```

The middleware stays off while both lists are empty, which is deliberate: an `X-Forwarded-For`
trusted from an undeclared source lets a caller forge a new partition per request and bypass the
limits entirely. Only declare addresses you actually control.

### 3. Run the API

```bash
cd API
dotnet run
```

Migrations are applied and seed data written on startup. The API listens on https://localhost:5001,
and the Hangfire dashboard is at `/hangfire` (open in Development, `Admin` role required
otherwise).

### 4. Run the client

```bash
cd client
npm install
npm start
```

The dev server is HTTPS and expects a certificate pair at `client/ssl/localhost.pem` and
`client/ssl/localhost-key.pem`, which are gitignored. Generate them with
[mkcert](https://github.com/FiloSottile/mkcert) before the first run:

```bash
mkdir -p client/ssl && cd client/ssl
mkcert -install
mkcert -cert-file localhost.pem -key-file localhost-key.pem localhost
```

The app is at https://localhost:4200.

## Tests

```bash
dotnet test                                              # 205 backend tests, Docker required
dotnet test --filter "Category!=Integration"             # 191 of them, no Docker
cd client && npm test -- --watch=false                   # 38 client tests
```

The 14 integration tests run against real PostgreSQL in a throwaway Testcontainers instance rather
than an in-memory provider, because none of what they assert is observable without real row
semantics:

* `RefreshSessionConcurrencyTests` drives two real connections at a single refresh token and
  asserts that exactly one successor is issued, that both callers are still served, and that the
  predecessor points at the one successor.
* `ExternalLoginConcurrencyTests` covers concurrent Google signups that derive the same username,
  and two signups racing for one email. It is the only thing that reaches the savepoint rollback
  and the raw unique-violation branch in the signup path: run serially, Identity's own validator
  absorbs every collision before the INSERT ever runs.
* `OutboxConcurrencyTests` covers competing dispatchers against the same message, an expired lease
  double-claim, and the reaper.
* `AuctionSettlementJobIntegrationTests` covers the claim and lease path under contention.

The unit suite keeps real row semantics behind an in-memory store instead of faking the repository
outright, so the grace, cascade, lockout, and lost-claim branches are each covered against actual
state. CI runs both suites, plus `dotnet format --verify-no-changes`, on every push and PR.

There is slightly more test code than production code, which is mostly a consequence of the
concurrency work: none of it can be verified by reading it.

## Project layout

```
API/            ASP.NET Core API
  Controllers/  Auctions, bids, payments, messages, account, users
  Services/     Domain services, Hangfire jobs, outbox dispatch, email
  Entities/     EF Core model
  Data/         DbContext, repositories, migrations
  Validation/   Redirect policy for the OAuth callback
  Views/Emails/ Razor email templates
  SignalR/      Presence hub
API.Tests/      Unit and PostgreSQL integration tests
client/         Angular app
docs/           Design notes
```

## Not built, on purpose

Worth stating rather than leaving to be discovered:

* **No email confirmation.** Password registration trusts the address given; only Google accounts
  arrive verified. This is the largest remaining gap, and it is what a trustworthy password reset
  would have to be built on.
* **No password reset or change flow at all**, which is why the gap above has not bitten yet.
* **No account linking** between password and federated sign-in, for the reason given above.
* **No live bid updates.** A bidder sees a new high bid on their next request, not pushed. SignalR
  is wired up for presence only, and the `Bids` table is deliberately the log rather than a stream;
  [docs/kafka.md](docs/kafka.md) is the argument for why.
* **No audit trail for rejected bids.** `PlaceBid` returns on a failed guard without recording
  anything, so only accepted bids leave a trace.
* **No horizontal scale.** Settlement and outbox dispatch are safe to run on several workers
  because they coordinate through Postgres, but presence tracking holds state in process and the
  rate limiter counts in memory, so both are per-instance. Running more than one API instance
  needs a SignalR backplane and a shared counter store first.
* **Not deployed.** There is no production profile: `IEmailSender` is registered in Development
  only, so the email handlers would fail to construct anywhere else. That guard is the first thing
  a deployment has to change.
