using API.Entities;
using API.Tests.Payments;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Stripe;
using Xunit;

namespace API.Tests.Services;

public class PaymentWebhookTests
{
    private const string SessionId = "cs_test_123";

    /// <summary>Builds a pending attempt (+ parent payment) and registers it for the given session id.</summary>
    private static (Payment payment, PaymentAttempt attempt) SeedAttempt(
        PaymentTestContext ctx, PaymentAttemptStatus status = PaymentAttemptStatus.Pending, string sessionId = SessionId)
    {
        var payment = new Payment
        {
            PaymentId = 1,
            AuctionId = 1,
            UserId = "winner",
            Amount = 150m,
            Status = PaymentStatus.Pending
        };
        var attempt = new PaymentAttempt
        {
            AttemptId = 1,
            PaymentId = 1,
            Amount = 150m,
            Status = status,
            StripeSessionId = sessionId,
            Payment = payment
        };
        payment.Attempts.Add(attempt);
        ctx.PaymentRepo.GetAttemptByStripeSessionIdAsync(sessionId).Returns(attempt);
        // The completed-payment producer reads the auction to build the outbox payload
        // (seller + item name). Expired-path tests never reach this branch.
        ctx.AuctionRepo.GetAuctionAsync(payment.AuctionId).Returns(new Auction
        {
            AuctionId = payment.AuctionId,
            ItemName = "Test Item",
            StartingPrice = 100m,
            SellerId = "seller",
            StartTime = DateTimeOffset.UtcNow.AddDays(-2),
            EndTime = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        return (payment, attempt);
    }

    private static Task Deliver(PaymentTestContext ctx, string json) =>
        ctx.Service.HandleWebhook(json, StripeWebhookSignature.Sign(json, PaymentTestContext.WebhookSecret));

    [Fact]
    public async Task Completed_MarksAttemptCompletedAndPaymentPaid()
    {
        var ctx = new PaymentTestContext();
        var (payment, attempt) = SeedAttempt(ctx);

        await Deliver(ctx, StripeWebhookSignature.CompletedEventJson(SessionId));

        Assert.Equal(PaymentAttemptStatus.Completed, attempt.Status);
        Assert.NotNull(attempt.CompletedAt);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.NotNull(payment.CompletedAt);
    }

    [Fact]
    public async Task Completed_DeliveredTwice_IsIdempotent()
    {
        var ctx = new PaymentTestContext();
        var (payment, attempt) = SeedAttempt(ctx);

        await Deliver(ctx, StripeWebhookSignature.CompletedEventJson(SessionId));
        var firstCompletedAt = payment.CompletedAt;
        await Deliver(ctx, StripeWebhookSignature.CompletedEventJson(SessionId));

        Assert.Equal(PaymentAttemptStatus.Completed, attempt.Status);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(firstCompletedAt, payment.CompletedAt); // MarkPaid guard: not overwritten
    }

    [Fact]
    public async Task Completed_WhenSaveHitsUniqueViolation_SwallowsException()
    {
        // The partial unique index on a Completed attempt can raise 23505 on redelivery races.
        // The handler catches it; here we fake the exception so no Postgres is needed (§3b proves
        // the real index actually raises it).
        var ctx = new PaymentTestContext();
        SeedAttempt(ctx);
        var dbEx = new DbUpdateException("duplicate",
            new PostgresException("duplicate key value", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
        ctx.UnitOfWork.CompleteAsync().ThrowsAsync(dbEx);

        var ex = await Record.ExceptionAsync(() => Deliver(ctx, StripeWebhookSignature.CompletedEventJson(SessionId)));

        Assert.Null(ex);
    }

    [Fact]
    public async Task Expired_OnPendingAttempt_MarksExpired()
    {
        var ctx = new PaymentTestContext();
        var (_, attempt) = SeedAttempt(ctx, PaymentAttemptStatus.Pending);

        await Deliver(ctx, StripeWebhookSignature.ExpiredEventJson(SessionId));

        Assert.Equal(PaymentAttemptStatus.Expired, attempt.Status);
    }

    [Fact]
    public async Task Expired_OnAlreadyCompletedAttempt_LeavesItUnchanged()
    {
        var ctx = new PaymentTestContext();
        var (_, attempt) = SeedAttempt(ctx, PaymentAttemptStatus.Completed);

        await Deliver(ctx, StripeWebhookSignature.ExpiredEventJson(SessionId));

        Assert.Equal(PaymentAttemptStatus.Completed, attempt.Status);
    }

    /// <summary>
    /// Characterization test pinning CURRENT behavior: a Completed event for a Cancelled attempt
    /// flips it to Completed and marks the payment Paid, because the handler treats any non-Completed
    /// status as fair game. `Cancelled` is never actually assigned in production today — see the
    /// "documented gaps" note in the plan / TESTING.md. If cancellation handling is added, revisit this.
    /// </summary>
    [Fact]
    public async Task Completed_ForCancelledAttempt_CurrentlyStillCompletesAndPays()
    {
        var ctx = new PaymentTestContext();
        var (payment, attempt) = SeedAttempt(ctx, PaymentAttemptStatus.Cancelled);

        await Deliver(ctx, StripeWebhookSignature.CompletedEventJson(SessionId));

        Assert.Equal(PaymentAttemptStatus.Completed, attempt.Status);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
    }

    [Fact]
    public async Task Completed_ForUnknownSession_IsNoOp()
    {
        var ctx = new PaymentTestContext();
        ctx.PaymentRepo.GetAttemptByStripeSessionIdAsync(Arg.Any<string>()).Returns((PaymentAttempt?)null);

        var ex = await Record.ExceptionAsync(() => Deliver(ctx, StripeWebhookSignature.CompletedEventJson("cs_unknown")));

        Assert.Null(ex);
    }

    [Fact]
    public async Task Webhook_WithBadSignature_ThrowsStripeException()
    {
        var ctx = new PaymentTestContext();
        SeedAttempt(ctx);
        var json = StripeWebhookSignature.CompletedEventJson(SessionId);

        await Assert.ThrowsAsync<StripeException>(() => ctx.Service.HandleWebhook(json, "t=123,v1=deadbeef"));
    }

    [Fact]
    public async Task Webhook_ForNonSessionEvent_IsIgnored()
    {
        var ctx = new PaymentTestContext();
        var json = StripeWebhookSignature.EventJson("payment_intent.succeeded", "pi_test", objectType: "payment_intent");

        await Deliver(ctx, json);

        await ctx.PaymentRepo.DidNotReceive().GetAttemptByStripeSessionIdAsync(Arg.Any<string>());
    }
}
