using API.Core;
using API.Data;
using API.DTOs;
using API.Entities;
using API.Tests.Payments;
using API.Tests.Payments.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Exercises the Postgres-specific unique-violation guards that EF InMemory cannot reproduce.
/// Opt-in: gated behind the "Integration" trait (Docker required) so the fast suite can skip it
/// via <c>--filter Category!=Integration</c>.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class PaymentConcurrencyTests(PostgresFixture fixture)
{
    // ---- 3a: concurrent checkout for the same auction ----
    [Fact]
    public async Task ConcurrentCheckout_ForSameAuction_LeavesExactlyOnePaymentAndBothSucceed()
    {
        var auctionId = await SeedEndedAuctionAsync();

        await using var dbA = fixture.CreateDbContext();
        await using var dbB = fixture.CreateDbContext();
        // Real Stripe returns a unique session id per Checkout Session; mirror that so the two
        // racers don't collide on the (legitimate) StripeSessionId unique index.
        var svcA = fixture.CreatePaymentService(dbA, new CapturingStripeHttpClient(SessionJson("cs_test_A")));
        var svcB = fixture.CreatePaymentService(dbB, new CapturingStripeHttpClient(SessionJson("cs_test_B")));

        var outcomes = await Task.WhenAll(
            Attempt(() => svcA.CreateCheckoutSession(auctionId, "u1")),
            Attempt(() => svcB.CreateCheckoutSession(auctionId, "u1")));

        // Neither call should surface an unhandled exception...
        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.All(outcomes, o => Assert.True(o.Success, $"call failed: {o.Reason}"));

        // ...and the unique index must have kept it to a single payment row.
        await using var verify = fixture.CreateDbContext();
        var payments = await verify.Payments.CountAsync(p => p.AuctionId == auctionId, TestContext.Current.CancellationToken);
        Assert.Equal(1, payments);
    }

    // ---- 3b: duplicate completed attempt (partial unique index on Status = Completed) ----
    [Fact]
    public async Task Webhook_SecondCompletedAttempt_IsRejectedByPartialIndexAndSwallowed()
    {
        int paymentId;
        await using (var seed = fixture.CreateDbContext())
        {
            await EnsureUserAsync(seed, "u1");
            var auctionId = await InsertAuctionAsync(seed, "u1");
            var payment = new Payment
            {
                AuctionId = auctionId,
                UserId = "u1",
                Amount = 150m,
                Status = PaymentStatus.Paid,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Attempts =
                {
                    new PaymentAttempt { PaymentId = 0, Amount = 150m, Status = PaymentAttemptStatus.Completed, StripeSessionId = "cs_first", CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow },
                    new PaymentAttempt { PaymentId = 0, Amount = 150m, Status = PaymentAttemptStatus.Pending, StripeSessionId = "cs_second", CreatedAt = DateTimeOffset.UtcNow }
                }
            };
            seed.Payments.Add(payment);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            paymentId = payment.PaymentId;
        }

        await using var db = fixture.CreateDbContext();
        var svc = fixture.CreatePaymentService(db, new CapturingStripeHttpClient(PaymentTestContext.DefaultSessionJson));
        var json = StripeWebhookSignature.CompletedEventJson("cs_second");

        var ex = await Record.ExceptionAsync(() =>
            svc.HandleWebhook(json, StripeWebhookSignature.Sign(json, PaymentTestContext.WebhookSecret)));

        Assert.Null(ex); // unique-violation caught and logged, not propagated

        await using var verify = fixture.CreateDbContext();
        var completed = await verify.PaymentAttempts
            .CountAsync(a => a.PaymentId == paymentId && a.Status == PaymentAttemptStatus.Completed, TestContext.Current.CancellationToken);
        Assert.Equal(1, completed);
    }

    // ---- helpers ----

    private static string SessionJson(string id) =>
        $"{{\"id\":\"{id}\",\"object\":\"checkout.session\",\"url\":\"https://checkout.stripe.test/pay/{id}\"}}";

    private async Task<int> SeedEndedAuctionAsync()
    {
        await using var db = fixture.CreateDbContext();
        await EnsureUserAsync(db, "u1");
        return await InsertAuctionAsync(db, "u1");
    }

    private static async Task EnsureUserAsync(AppDbContext db, string id)
    {
        if (await db.Users.AnyAsync(u => u.Id == id)) return;
        db.Users.Add(new AppUser
        {
            Id = id,
            DisplayName = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            Email = $"{id}@test.com",
            NormalizedEmail = $"{id}@TEST.COM".ToUpperInvariant()
        });
        await db.SaveChangesAsync();
    }

    private static async Task<int> InsertAuctionAsync(AppDbContext db, string userId)
    {
        var auction = new Auction
        {
            ItemName = "Test Item",
            StartingPrice = 100m,
            SellerId = userId,
            StartTime = DateTimeOffset.UtcNow.AddDays(-2),
            EndTime = DateTimeOffset.UtcNow.AddMinutes(-5), // ended
            CurrentHighBid = 150m,
            CurrentHighBidderId = userId
        };
        db.Auctions.Add(auction);
        await db.SaveChangesAsync();
        return auction.AuctionId;
    }

    private static async Task<(bool Success, string? Reason, Exception? Error)> Attempt(Func<Task<Result<CreatePaymentResponseDto>>> call)
    {
        try
        {
            var result = await call();
            return (result.IsSuccess, result.Reason?.ToString(), null);
        }
        catch (Exception e)
        {
            return (false, null, e);
        }
    }
}
