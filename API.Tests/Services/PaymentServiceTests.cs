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
        Assert.Contains("metadata[auction_id]=1", body);
        // AttemptId is not known when the session is built (it is created before the attempt is
        // persisted), so we correlate on the payment instead.
        Assert.Contains("client_reference_id=100", body);
        // Success lands on the order page, keyed by the freshly assigned PaymentId (100 in this fixture).
        Assert.Contains($"success_url={PaymentTestContext.ClientAppUrl}/orders/100?session_id=", body);
        Assert.Contains($"cancel_url={PaymentTestContext.ClientAppUrl}/auctions/1?cancelled=true", body);
        // Idempotency key is a per-call Guid now, not tied to the (not-yet-assigned) AttemptId.
        Assert.True(Guid.TryParse(ctx.Http.LastRequest!.StripeHeaders["Idempotency-Key"], out _));
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
    public async Task CreateCheckoutSession_WhenStripeThrows_ReturnsInternalErrorAndPersistsNoAttempt()
    {
        var ctx = new PaymentTestContext(stripeThrows: new StripeException("card_declined"));
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction("winner", 150m));
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns((Payment?)null);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.InternalError, result.Reason);
        // Session is created before the attempt is persisted, so a Stripe failure leaves no attempt
        // at all — no Pending/null-session row to strand the payment behind the Pending index.
        Assert.Empty(ctx.AddedPayment!.Attempts);
        Assert.Equal(PaymentStatus.Pending, ctx.AddedPayment.Status); // never flipped to Paid
    }

    // ---- r8: reuse-or-refuse (a winner is charged at most once) ----

    // The reported bug: an already-paid payment must be refused before anything is created,
    // regardless of how the winner re-entered (e.g. clicking the "You won" email link again).
    [Fact]
    public async Task CreateCheckoutSession_WhenAlreadyPaid_RefusesWithoutTouchingStripe()
    {
        var ctx = new PaymentTestContext();
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction("winner", 150m));
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(new Payment
        {
            PaymentId = 77, AuctionId = 1, UserId = "winner", Amount = 150m, Status = PaymentStatus.Paid
        });

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Conflict, result.Reason);
        Assert.Null(ctx.Http.LastRequest);                       // never reached Stripe
        ctx.PaymentRepo.DidNotReceive().Add(Arg.Any<Payment>());
    }

    // A live session is reused, not duplicated: the winner gets the same URL back and no second attempt.
    [Fact]
    public async Task CreateCheckoutSession_WithOpenSession_ReturnsSameUrlWithoutNewAttempt()
    {
        const string openSession =
            "{\"id\":\"cs_existing\",\"object\":\"checkout.session\",\"status\":\"open\"," +
            "\"url\":\"https://checkout.stripe.test/pay/cs_existing\"}";
        var ctx = new PaymentTestContext(stripeResponseJson: openSession);
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction("winner", 150m));
        var payment = PendingPaymentWithSession("cs_existing");
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(payment);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://checkout.stripe.test/pay/cs_existing", result.Value!.CheckoutUrl);
        Assert.Single(payment.Attempts);                         // reused, not a fresh attempt
        ctx.PaymentRepo.DidNotReceive().Add(Arg.Any<Payment>());
    }

    // Closes the webhook-lag race: our row still says Pending, but Stripe's session is complete/paid,
    // and Stripe is authoritative — so we mark paid and refuse rather than opening a second session.
    [Fact]
    public async Task CreateCheckoutSession_WhenExistingSessionAlreadyPaid_MarksPaidAndRefuses()
    {
        const string paidSession =
            "{\"id\":\"cs_done\",\"object\":\"checkout.session\",\"status\":\"complete\"," +
            "\"payment_status\":\"paid\",\"url\":\"https://checkout.stripe.test/pay/cs_done\"}";
        var ctx = new PaymentTestContext(stripeResponseJson: paidSession);
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction("winner", 150m));
        var payment = PendingPaymentWithSession("cs_done");
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(payment);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Conflict, result.Reason);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(PaymentAttemptStatus.Completed, payment.Attempts.Single().Status);
    }

    // Fail closed: if we can't reach Stripe to check the existing session, never fall through to
    // minting a second one.
    [Fact]
    public async Task CreateCheckoutSession_WhenSessionLookupFails_FailsClosedWithoutNewAttempt()
    {
        var ctx = new PaymentTestContext(stripeThrows: new StripeException("stripe unavailable"));
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction("winner", 150m));
        var payment = PendingPaymentWithSession("cs_x");
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(payment);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.InternalError, result.Reason);
        Assert.Single(payment.Attempts);                         // no second attempt
        ctx.PaymentRepo.DidNotReceive().Add(Arg.Any<Payment>());
    }

    // An expired session is reaped and a fresh one minted — the winner can still pay within the window.
    [Fact]
    public async Task CreateCheckoutSession_WhenExistingSessionExpired_MintsFreshSession()
    {
        const string expiredSession =
            "{\"id\":\"cs_old\",\"object\":\"checkout.session\",\"status\":\"expired\"," +
            "\"url\":\"https://checkout.stripe.test/pay/cs_old\"}";
        var ctx = new PaymentTestContext(stripeResponseJson: expiredSession);
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(EndedAuction("winner", 150m));
        var payment = PendingPaymentWithSession("cs_old");
        ctx.TrackForIdAssignment(payment);
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(payment);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, payment.Attempts.Count);
        Assert.Contains(payment.Attempts, a => a.Status == PaymentAttemptStatus.Expired);
        Assert.Contains(payment.Attempts, a => a.Status == PaymentAttemptStatus.Pending);
        ctx.PaymentRepo.DidNotReceive().Add(Arg.Any<Payment>());
    }

    // Past the pay window (auction ended > 7 days ago), refuse before any Stripe call.
    [Fact]
    public async Task CreateCheckoutSession_AfterPayWindowClosed_ReturnsConflict()
    {
        var ctx = new PaymentTestContext();
        var auction = EndedAuction("winner", 150m);
        auction.EndTime = DateTimeOffset.UtcNow.AddDays(-8);      // default 7-day window elapsed
        ctx.AuctionRepo.GetAuctionAsync(1).Returns(auction);
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns((Payment?)null);

        var result = await ctx.Service.CreateCheckoutSession(1, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Conflict, result.Reason);
        Assert.Null(ctx.Http.LastRequest);
        ctx.PaymentRepo.DidNotReceive().Add(Arg.Any<Payment>());
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

    // ---- effective status ----
    // Payment.Status is only Pending|Paid, so a failed checkout is invisible on the payment itself.
    // GetPaymentStatus folds the latest attempt up so the client can tell "waiting on the webhook"
    // apart from a real failure. These cover the branch that was unreachable before.

    [Theory]
    [InlineData(PaymentAttemptStatus.Failed, "Failed")]
    [InlineData(PaymentAttemptStatus.Cancelled, "Failed")]
    [InlineData(PaymentAttemptStatus.Expired, "Expired")]
    [InlineData(PaymentAttemptStatus.Pending, "Pending")]
    public async Task GetPaymentStatus_WhenPending_FoldsInLatestAttempt(
        PaymentAttemptStatus attemptStatus, string expected)
    {
        var ctx = new PaymentTestContext();
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(PendingPaymentWith(
            (attemptStatus, DateTimeOffset.UtcNow)));

        var result = await ctx.Service.GetPaymentStatus(1, "winner");

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value!.Status);
    }

    [Fact]
    public async Task GetPaymentStatus_WhenPendingWithNoAttempts_ReturnsPending()
    {
        var ctx = new PaymentTestContext();
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(PendingPaymentWith());

        var result = await ctx.Service.GetPaymentStatus(1, "winner");

        Assert.Equal("Pending", result.Value!.Status);
    }

    [Fact]
    public async Task GetPaymentStatus_RetryAfterFailedAttempt_ReadsPending()
    {
        var ctx = new PaymentTestContext();
        var now = DateTimeOffset.UtcNow;
        // Newest attempt wins: a winner retrying a failed checkout must not sit on a "Failed"
        // stepper while a perfectly healthy second attempt is in flight.
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(PendingPaymentWith(
            (PaymentAttemptStatus.Failed, now.AddMinutes(-5)),
            (PaymentAttemptStatus.Pending, now)));

        var result = await ctx.Service.GetPaymentStatus(1, "winner");

        Assert.Equal("Pending", result.Value!.Status);
    }

    [Fact]
    public async Task GetPaymentStatus_PaidWinsOverAnyEarlierFailedAttempt()
    {
        var ctx = new PaymentTestContext();
        var now = DateTimeOffset.UtcNow;
        // MarkPaid is the terminal latch: once the payment is Paid, a stale Expired webhook for an
        // abandoned earlier session must not drag the reported status backwards.
        var payment = PendingPaymentWith(
            (PaymentAttemptStatus.Completed, now.AddMinutes(-5)),
            (PaymentAttemptStatus.Expired, now));
        payment.MarkPaid(now);
        ctx.PaymentRepo.GetByAuctionIdAsync(1).Returns(payment);

        var result = await ctx.Service.GetPaymentStatus(1, "winner");

        Assert.Equal("Paid", result.Value!.Status);
    }

    // ---- GetOrder / GetOrdersForUser ----

    // A stranger must not be able to tell an order apart from one that never existed, so a
    // wrong-owner read returns NotFound, not Forbidden, which would confirm it exists.
    [Fact]
    public async Task GetOrder_WhenNotOwner_ReturnsNotFound()
    {
        var ctx = new PaymentTestContext();
        ctx.PaymentRepo.GetByIdAsync(100).Returns(OrderPayment(userId: "owner"));

        var result = await ctx.Service.GetOrder(100, "intruder");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.NotFound, result.Reason);
    }

    [Fact]
    public async Task GetOrder_WhenMissing_ReturnsNotFound()
    {
        var ctx = new PaymentTestContext();
        ctx.PaymentRepo.GetByIdAsync(100).Returns((Payment?)null);

        var result = await ctx.Service.GetOrder(100, "winner");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.NotFound, result.Reason);
    }

    [Fact]
    public async Task GetOrder_WhenOwner_MapsAuctionFieldsOntoOrder()
    {
        var ctx = new PaymentTestContext();
        ctx.PaymentRepo.GetByIdAsync(100).Returns(OrderPayment(userId: "winner"));

        var result = await ctx.Service.GetOrder(100, "winner");

        Assert.True(result.IsSuccess);
        Assert.Equal("Test Item", result.Value!.ItemName);
        Assert.Equal("Seller", result.Value.SellerName);
        Assert.Equal(1, result.Value.AuctionId);
        Assert.Equal("Paid", result.Value.Status);
    }

    // GetForUserAsync already filters + orders in SQL, so the service test only proves the
    // projection runs over the whole set. Ordering belongs in a repository/integration test.
    [Fact]
    public async Task GetOrdersForUser_ProjectsEveryPayment()
    {
        var ctx = new PaymentTestContext();
        ctx.PaymentRepo.GetForUserAsync("winner")
            .Returns([OrderPayment("winner"), OrderPayment("winner")]);

        var orders = await ctx.Service.GetOrdersForUser("winner");

        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.Equal("Test Item", o.ItemName));
    }

    // A Paid payment carrying the auction + seller the order projection reads. GetByIdAsync eager
    // loads both in production; here we build them inline.
    private static Payment OrderPayment(string userId = "winner") => new()
    {
        PaymentId = 100,
        AuctionId = 1,
        UserId = userId,
        Amount = 150m,
        Status = PaymentStatus.Paid,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        CompletedAt = DateTimeOffset.UtcNow,
        Auction = new Auction
        {
            AuctionId = 1,
            ItemName = "Test Item",
            StartingPrice = 100m,
            SellerId = "seller",
            StartTime = DateTimeOffset.UtcNow.AddDays(-2),
            EndTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            Seller = new AppUser { DisplayName = "Seller" }
        }
    };

    private static Payment PendingPaymentWith(params (PaymentAttemptStatus Status, DateTimeOffset CreatedAt)[] attempts)
    {
        var payment = new Payment
        {
            AuctionId = 1,
            UserId = "winner",
            Amount = 150m,
            Status = PaymentStatus.Pending
        };

        foreach (var (status, createdAt) in attempts)
        {
            payment.Attempts.Add(new PaymentAttempt
            {
                PaymentId = 1,
                Amount = 150m,
                Status = status,
                CreatedAt = createdAt
            });
        }

        return payment;
    }

    // A Pending payment whose latest attempt already carries a Stripe session id — the state the
    // reuse path inspects. status lets a test stage an Expired attempt instead of Pending.
    private static Payment PendingPaymentWithSession(
        string sessionId, PaymentAttemptStatus status = PaymentAttemptStatus.Pending)
    {
        var payment = new Payment
        {
            PaymentId = 77,
            AuctionId = 1,
            UserId = "winner",
            Amount = 150m,
            Status = PaymentStatus.Pending
        };
        payment.Attempts.Add(new PaymentAttempt
        {
            PaymentId = 77,
            Amount = 150m,
            Status = status,
            StripeSessionId = sessionId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return payment;
    }
}
