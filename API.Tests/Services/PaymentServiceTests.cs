using System.Net;
using API.Core;
using API.Entities;
using API.Tests.Payments;
using NSubstitute;
using Stripe;
using Xunit;

namespace API.Tests.Services;

public class PaymentServiceTests
{
    private static Auction EndedAuction(string? winnerId = "winner", decimal? highBid = 150.00m) => new()
    {
        AuctionId = 1,
        ItemName = "Test Item",
        StartingPrice = 100m,
        SellerId = "seller",
        StartTime = DateTimeOffset.UtcNow.AddDays(-2),
        EndTime = DateTimeOffset.UtcNow.AddMinutes(-5),
        CurrentHighBid = highBid,
        CurrentHighBidderId = winnerId
    };

    // ---- CreateCheckoutSession guards (return before touching Stripe) ----

    [Fact]
    public async Task CreateCheckoutSession_AuctionNotFound_ReturnsNotFound()
    {
        var ctx = new PaymentTestContext();
        ctx.AuctionRepo.GetAuctionAsync(1).Returns((Auction?)null);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.NotFound, result.Reason);
    }

    [Fact]
    public async Task CreateCheckoutSession_AuctionStillRunning_ReturnsConflict()
    {
        var ctx = new PaymentTestContext();
        var auction = EndedAuction();
        auction.EndTime = DateTimeOffset.UtcNow.AddHours(1); // not ended yet
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(auction);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Conflict, result.Reason);
    }

    [Fact]
    public async Task CreateCheckoutSession_NoBidder_ReturnsConflict()
    {
        var ctx = new PaymentTestContext();
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction(winnerId: null, highBid: null));

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Conflict, result.Reason);
    }

    [Fact]
    public async Task CreateCheckoutSession_CallerIsNotWinner_ReturnsForbidden()
    {
        var ctx = new PaymentTestContext();
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction(winnerId: "someone-else"));

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Forbidden, result.Reason);
    }

    // ---- CreateCheckoutSession happy path ----

    [Fact]
    public async Task CreateCheckoutSession_WhenWinner_CreatesPaymentAndReturnsCheckoutUrl()
    {
        var ctx = new PaymentTestContext();
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction("winner", 150.00m));
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns((Payment?)null);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://checkout.stripe.test/pay/cs_test_123", result.Value!.CheckoutUrl);
        ctx.PaymentRepo.Received(1).Add(Arg.Any<Payment>());

        var payment = ctx.AddedPayment!;
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(150.00m, payment.Amount);
        Assert.Equal("winner", payment.UserId);
        Assert.Equal("cs_test_123", payment.Attempts.Single().StripeSessionId);

        // Outgoing Stripe request carries the right amount / currency / metadata / urls.
        var body = WebUtility.UrlDecode(ctx.Http.LastRequestBody!);
        Assert.Contains("[unit_amount]=15000", body); // 150.00 dollars -> cents (nested form key)
        Assert.Contains("[currency]=cad", body);
        Assert.Contains("metadata[payment_id]=100", body);
        Assert.Contains("metadata[attempt_id]=500", body);
        Assert.Contains("metadata[auction_id]=1", body);
        Assert.Contains($"success_url={PaymentTestContext.ClientAppUrl}/auctions/1?session_id=", body);
        Assert.Contains($"cancel_url={PaymentTestContext.ClientAppUrl}/auctions/1?cancelled=true", body);
        Assert.Equal("attempt-500", ctx.Http.LastRequest!.StripeHeaders["Idempotency-Key"]);
    }

    [Fact]
    public async Task CreateCheckoutSession_WithExistingPendingPayment_AddsAttemptWithoutDuplicatingPayment()
    {
        var ctx = new PaymentTestContext();
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction("winner", 150m));
        var existing = new Payment
        {
            PaymentId = 77,
            AuctionId = 1,
            UserId = "winner",
            Amount = 150m,
            Status = PaymentStatus.Pending
        };
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(existing);
        ctx.TrackForIdAssignment(existing);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.True(result.IsSuccess);
        ctx.PaymentRepo.DidNotReceive().Add(Arg.Any<Payment>());
        Assert.Single(existing.Attempts);
        Assert.Equal("cs_test_123", existing.Attempts.Single().StripeSessionId);
    }

    [Fact]
    public async Task CreateCheckoutSession_WhenStripeThrows_MarksAttemptFailedAndReturnsInternalError()
    {
        var ctx = new PaymentTestContext(stripeThrows: new StripeException("card_declined"));
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction("winner", 150m));
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns((Payment?)null);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.InternalError, result.Reason);
        Assert.Equal(PaymentAttemptStatus.Failed, ctx.AddedPayment!.Attempts.Single().Status);
        Assert.Equal(PaymentStatus.Pending, ctx.AddedPayment.Status); // never flipped to Paid
    }

    // ---- GetPaymentStatus ----

    [Fact]
    public async Task GetPaymentStatus_NoPayment_ReturnsNotFound()
    {
        var ctx = new PaymentTestContext();
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns((Payment?)null);

        var result = await ctx.Service.GetPaymentStatus(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.NotFound, result.Reason);
    }

    [Fact]
    public async Task GetPaymentStatus_WrongUser_ReturnsForbidden()
    {
        var ctx = new PaymentTestContext();
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(new Payment
        {
            AuctionId = 1,
            UserId = "owner",
            Amount = 150m,
            Status = PaymentStatus.Pending
        });

        var result = await ctx.Service.GetPaymentStatus(1, "intruder");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Forbidden, result.Reason);
    }

    [Fact]
    public async Task GetPaymentStatus_WhenPaid_ReturnsMappedDto()
    {
        var ctx = new PaymentTestContext();
        var completedAt = DateTimeOffset.UtcNow;
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(new Payment
        {
            AuctionId = 1,
            UserId = "winner",
            Amount = 150m,
            Status = PaymentStatus.Paid,
            CompletedAt = completedAt
        });

        var result = await ctx.Service.GetPaymentStatus(1, "winner");

        Assert.True(result.IsSuccess);
        Assert.Equal("Paid", result.Value!.Status);
        Assert.Equal(150m, result.Value.Amount);
        Assert.Equal(completedAt, result.Value.CompletedAt);
    }
}
