# Transactional email

**Scope:** the two emails the system sends, and the provider-agnostic layer behind them
**Companion docs:** [winner-email-testing.md](winner-email-testing.md) for the manual runbook, [payment-outbox-explained.md](payment-outbox-explained.md) for the outbox they ride on
**Guiding constraint:** the default path must be free and must work on a fresh `git clone` with no accounts and no secrets

Two emails leave the system, both from outbox handlers: a **winner** email when settlement finds an
auction with a high bidder, and a **receipt** when Stripe confirms the winner paid. Neither names a
vendor. Both go through one seam (`IEmailSender`) and one renderer (`IEmailTemplateRenderer`), and
in development both land in a local SMTP catcher rather than a real inbox.

---

## 1. Why the outbox and not a direct send

An email is the textbook "external side effect that must not be lost and must not roll back the
thing that caused it." Sending inline from `AuctionSettlementJob` would mean either holding the
settlement transaction open across an SMTP round trip, or sending after the commit and losing the
email whenever the process dies in the gap.

So both emails are outbox rows, written in the same transaction as the state change that owes them:

| Email | Producer | Outbox `Type` | Handler |
|---|---|---|---|
| Auction won | `AuctionSettlementJob.RunAsync`, co-committed with `Finalize()` and the in-app "you won" `Message` | `AuctionEnded` | `AuctionEndedHandler` |
| Payment receipt | `PaymentService.HandleWebhook`, co-committed with `MarkPaid` | `PaymentReceipt` | `PaymentReceiptHandler` |

A ~1 minute dispatch delay is acceptable for both: nobody is sitting on a spinner waiting for
either one. An email where the user *is* waiting, a password reset being the obvious case, would
have to be sent inline instead. There is no such flow in the codebase.

Adding a side effect is one handler plus one DI registration; the dispatcher resolves handlers by
matching `IOutboxHandler.Type` to `OutboxMessage.Type` and needs no changes.

---

## 2. The seam

```csharp
// API/Interfaces/IEmailSender.cs
public interface IEmailSender
{
    /// <summary>
    /// Outbox delivery is at-least-once. idempotencyKey names the logical send so a provider that
    /// supports idempotent requests can collapse repeats. MailKit cannot: SMTP has no such mechanism.
    /// </summary>
    Task SendAsync(string toEmail, string subject, string htmlBody, string idempotencyKey, CancellationToken ct);
}
```

Everything above the seam names only `IEmailSender`. Which transport is wired in is one line of DI,
so swapping to a hosted provider's HTTP API is a new class and a registration, not a change to any
auction code.

**The one implementation is `MailKitEmailSender`**, SMTP over MailKit (`System.Net.Mail.SmtpClient`
is officially obsolete). One implementation covers both environments because SMTP is SMTP: it
points at Mailpit locally and would point at a relay's host and credentials in production, selected
entirely by the `Email` config section bound to `EmailOptions`.

The deliberate cost of choosing SMTP is in §4.

---

## 3. Templating

Both emails render Razor `.cshtml` through `Razor.Templating.Core`, behind
`IEmailTemplateRenderer`. Three reasons it earns its keep over string concatenation for only two
emails:

- **Auto HTML-encoding.** `@Model.ItemName` is user-supplied (a seller names their own item) and
  Razor escapes it by default, which removes a manual `HtmlEncoder` call and the injection and
  broken-markup risk that comes with forgetting one.
- **A shared layout.** `_Layout.cshtml` holds the branding, header and footer; each email is a body
  fragment and a typed model record.
- **It is the same `.cshtml` the rest of ASP.NET speaks**, so there is no second template language
  in the repo.

The runtime view compilation it costs is irrelevant at two templates and one send per auction.

```
API/Views/Emails/
  _Layout.cshtml    dark table-based shell, inline styles for mail-client compatibility
  Winner.cshtml     "you won", winning-bid panel, pay CTA      → WinnerEmailModel
  Receipt.cshtml    "paid", order panel, view-order CTA        → ReceiptEmailModel
```

Table layout with inline styles throughout, rather than the stylesheet a web page would use,
because Gmail and Outlook strip `<style>` blocks and ignore most modern CSS.

Both CTAs deep-link back into the client rather than anywhere vendor-hosted: the winner email opens
`/auctions/{id}?pay=1`, which pops the pay panel on arrival (the user still clicks Pay themselves),
and the receipt links to `/orders/{id}`, which is something a Stripe-generated receipt could not do.
Both are absolute URLs built from `ClientAppUrl`; the in-app `Message` written by the same producer
carries the *relative* form of the same link, because that one is handed to the client router and
must never carry a host.

---

## 4. Idempotency, and the limitation that isn't engineered away

Outbox delivery is **at-least-once**. If a lease lapses mid-handler, or the process dies between a
successful send and the `Status = Processed` commit, the row becomes due again and the handler runs
a second time.

For `PaymentCompletedHandler` that is harmless: its side effect is a DB write under a deterministic
id, the redelivery collides on the primary key, and the dispatcher reads `23505` as proof of prior
delivery ([payment-outbox-explained.md](payment-outbox-explained.md) §5). **Neither email handler
can use that mechanism.** There is no row to collide on, and the send happens *before* the
dispatcher commits, so even a colliding write would be detected too late to stop the message going
out.

Both handlers therefore pass a deterministic key, `auction-won-{AuctionId}` and
`payment-receipt-{PaymentId}`, and nothing acts on it today:

- **SMTP has no dedupe mechanism.** `MailKitEmailSender` puts the key in the `Message-Id` header,
  which is a threading header, not an instruction. A relay that sees the same id twice delivers
  twice.
- **An HTTP provider could honour it.** Resend and similar accept an `Idempotency-Key` header, so
  a second `IEmailSender` over HTTP would upgrade the key from naming-only to real suppression
  without touching a handler.

**The decision is to accept the duplicate.** Closing the window properly means exactly-once
delivery across a system boundary, which is not achievable; the alternatives are a sent-log table
(another dual-write, checked before a send that can still crash after it) or a provider that
dedupes for us. A duplicate "you won" email is low-harm, the crash window is milliseconds wide, and
the project is not deployed, so there is no production path where the HTTP sender would earn its
keep. Documented rather than hidden.

### Other handler guards

- **`AuctionEndedHandler` short-circuits on an already-paid auction.** A Buy Now buyer can win and
  pay before the minutely sweep runs, and should not then receive an email asking them to pay. The
  handler reads the payment first and returns if it is `Paid`, leaving the dispatcher to mark the
  row `Processed`.
- **Both handlers throw on a missing email address** rather than logging and returning. Failing the
  row costs nothing else, because neither owns a DB write that something else depends on. The branch
  is unreachable in practice: `options.User.RequireUniqueEmail` is set, and Identity's
  `UserValidator` rejects a null or empty address on both `CreateAsync` and `UpdateAsync`. It stays
  as an assertion because `IdentityUser.Email` is `string?` and the type cannot express the
  constraint.

---

## 5. Wiring

**Packages** (`API/API.csproj`): `MailKit` (pulls in MimeKit) and `Razor.Templating.Core`. The
project is already `Microsoft.NET.Sdk.Web`, so `.cshtml` under `API/Views/Emails/` compiles with no
extra SDK.

**DI** (`API/Program.cs`):

```csharp
builder.Services.AddScoped<IEmailTemplateRenderer, RazorEmailTemplateRenderer>();
builder.Services.AddRazorTemplating();
// Dev-only: the one implementation targets a local SMTP catcher. Other environments resolve no
// IEmailSender, so every outbox handler fails to construct. Needs an else before deploying.
if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

builder.Services.AddScoped<IOutboxHandler, AuctionEndedHandler>();
builder.Services.AddScoped<IOutboxHandler, PaymentReceiptHandler>();
```

That registration guard is a real limitation, not a subtlety: **outside Development no
`IEmailSender` is registered**, so both email handlers fail to construct and their rows retry until
they dead-letter. It is deliberate insurance against a half-configured deploy silently pointing at
`localhost:1025`, and it is the first thing that has to change if the project is ever deployed.

**Config** (`API/appsettings.Development.json`, gitignored; no secrets in it):

```json
"Email": {
  "Host": "localhost",
  "Port": 1025,
  "UseStartTls": false,
  "FromAddress": "auctions@localhost",
  "FromName": "Auction Site (dev)"
}
```

**Compose.** `compose.yaml` runs [Mailpit](https://mailpit.axllent.org/) alongside Postgres: SMTP
on 1025, a web UI on 8025. `docker compose up -d` is the whole setup, and the UI is a real inbox to
read caught mail in. It exercises the entire outbox → handler → seam path identically to a hosted
relay, and costs nothing and no account.

---

## 6. Where the behaviour is pinned

`AuctionEndedHandlerTests` and `PaymentReceiptHandlerTests` cover each handler against a
substituted `IEmailSender` and renderer: the model passed to the template (item, amount, seller,
deep link), the deterministic key, that neither stages a DB write, and each throw path (missing
user, missing payment, missing address, malformed payload). The already-paid short-circuit above is
the one branch with no test.

`OutboxDispatcherTests.A_failing_receipt_row_does_not_hold_back_the_notification_row` covers the
property the two-row split exists for: a mail failure retries the receipt alone and leaves the
seller's notification delivered.

[winner-email-testing.md](winner-email-testing.md) drives the whole chain by hand, settlement sweep
through to a rendered message in Mailpit, which is the part no unit test can show.
