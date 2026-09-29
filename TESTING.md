# Testing the payment / Stripe integration

Payment behaviour is covered at three layers. Layers 1 and 2 are automated; layer 3 is a manual
end-to-end check in Stripe test mode. For the settlement and email chain, see
[docs/winner-email-testing.md](docs/winner-email-testing.md).

## Layer 1: Fast tests (no Docker, Stripe or Postgres)

```bash
dotnet test AuctionSite.slnx --filter "Category!=Integration"
```

- `API.Tests/Services/PaymentServiceTests.cs`: checkout guards, happy path (asserts the outgoing
  Stripe request's amount / currency / metadata / idempotency key), attempt reuse, Stripe-failure
  path, and `GetPaymentStatus`.
- `API.Tests/Services/PaymentWebhookTests.cs`: real signed webhook payloads through the genuine
  `EventUtility.ConstructEvent` verification: completed, idempotent redelivery, expired, unknown
  session, bad signature, non-session event, and the `Cancelled` characterization test.
- `API.Tests/Entities/PaymentTests.cs`: `Payment.MarkPaid` idempotency.
- `API.Tests/Controllers/PaymentsControllerTests.cs`: auth, `HandleFailure` status mapping, and
  the raw-body webhook endpoint.

The Stripe seam is a fake `IHttpClient` (`API.Tests/Payments/Fakes/CapturingStripeHttpClient.cs`)
returning canned JSON and capturing the outgoing request: no network, no production changes.

## Layer 2: Postgres concurrency tests (Docker required)

```bash
dotnet test AuctionSite.slnx --filter "Category=Integration"
```

`API.Tests/Integration/PaymentConcurrencyTests.cs` uses **Testcontainers** to boot a throwaway
`postgres:16` and applies the real migrations, so the Postgres-specific unique indexes exist
(EF InMemory can't enforce them). It covers the two unique-violation guards:
1. concurrent checkout for one auction → exactly one `Payment`, both callers succeed;
2. a second `Completed` attempt rejected by the partial unique index → caught, not propagated.

## Layer 3: Manual end-to-end (Stripe test mode)

The Angular client can drive checkout from the auction page, but the API is easier to steer for a
deliberate test, so this walkthrough calls it directly with `curl` or any HTTP client. (There is no
OpenAPI endpoint exposed: `AddOpenApi()` is registered but never mapped.) You need a Stripe
**test-mode** account and the [Stripe CLI](https://stripe.com/docs/stripe-cli).

1. **Database**

   ```bash
   docker compose up -d db
   ```

2. **Stripe secret key** (User Secrets, from the `API/` project)

   ```bash
   dotnet user-secrets set "Stripe:SecretKey" "sk_test_..." --project API
   ```

3. **Webhook forwarding**, in a separate terminal:

   ```bash
   stripe login
   stripe listen --forward-to https://localhost:5001/api/payments/webhook
   ```

   Copy the printed `whsec_...` signing secret into User Secrets, then (re)start the API:

   ```bash
   dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..." --project API
   dotnet run --project API
   ```

4. **Get a JWT**: register/login via `POST /api/account/register` (or `/login`); use the returned
   token as `Authorization: Bearer <token>`.

5. **Create a payable auction**: create an auction, then win it. A **Buy Now** bid clamps
   `EndTime` to now, immediately making you the eligible winner.

6. **Start checkout**

   ```
   POST /api/payments/{auctionId}/checkout-session   (Bearer token)
   ```

   Open the returned `checkoutUrl` and pay with test card **4242 4242 4242 4242** (any future
   expiry, any CVC/postal).

7. **Confirm**: the `stripe listen` terminal shows `checkout.session.completed` forwarded to the
   API; then:

   ```
   GET /api/payments/{auctionId}/status   ->   { "status": "Paid", ... }
   ```

8. **Failure path**: repeat with declined card **4000 0000 0000 0002**.

### Caveats
- `stripe trigger checkout.session.expired` creates a **synthetic** session whose id won't match a
  real attempt, so the handler no-ops. True expiry behaviour is covered by the automated webhook
  test in layer 1, not by `trigger`.

## Known gaps

- **`PaymentAttemptStatus.Cancelled` is never assigned.** The value exists on the enum and nothing
  in production sets it. The `Cancelled` characterization test in layer 1 pins what the code does
  today (a completed event for a `Cancelled` attempt still completes and marks the payment `Paid`),
  so a change to cancellation handling has to be a deliberate one rather than an accident.
- **`checkout.session.async_payment_failed` has no handler.** The completion branch already guards
  on `session.PaymentStatus`, so an unconfirmed async payment is not marked paid, but nothing marks
  the attempt failed either: it sits `Pending` until the session expires. Latent while the Stripe
  account offers cards only; real the moment an async payment method is enabled.
- **A `checkout.session.completed` that Stripe never delivers is not reconciled**; see
  [docs/concurrency.md](docs/concurrency.md) §6.
