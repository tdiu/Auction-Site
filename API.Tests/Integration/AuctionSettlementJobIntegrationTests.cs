using API.Data;
using API.Entities;
using API.Interfaces;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Real-Postgres coverage of the sweeper: proves the finalize + winner Message + AuctionEnded outbox
/// row co-commit, that the FOR UPDATE SKIP LOCKED claim keeps two concurrent sweeps disjoint (the
/// regression for the inert [DisableConcurrentExecution]), and that a finalized row is never re-claimed.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class AuctionSettlementJobIntegrationTests(PostgresFixture fixture)
{
    private static AuctionSettlementJob CreateJob(AppDbContext db)
    {
        var uow = new UnitOfWork(
            db,
            Substitute.For<IUserRepository>(),
            new AuctionRepository(db),
            Substitute.For<IBidRepository>(),
            new PaymentRepository(db),
            new MessageRepository(db),
            new OutboxRepository(db));
        return new AuctionSettlementJob(uow, new ConfigurationBuilder().Build(),
            NullLogger<AuctionSettlementJob>.Instance);
    }

    [Fact]
    public async Task Sweep_finalizes_and_persists_winner_message_and_outbox_row_together()
    {
        // The collection shares one DB; clear outbox so the global "AuctionEnded" assertion is exact.
        await using (var clear = fixture.CreateDbContext())
            await clear.Database.ExecuteSqlRawAsync("DELETE FROM \"OutboxMessages\"",
                TestContext.Current.CancellationToken);

        await EnsureUserAsync("seller");
        await EnsureUserAsync("winner");
        var auctionId = await InsertEndedAuctionAsync("seller", "winner", 1500m);

        await using (var db = fixture.CreateDbContext())
            await CreateJob(db).RunAsync(TestContext.Current.CancellationToken);

        await using var verify = fixture.CreateDbContext();
        var ct = TestContext.Current.CancellationToken;

        var auction = await verify.Auctions.SingleAsync(a => a.AuctionId == auctionId, ct);
        Assert.NotNull(auction.FinalizedAt);

        var message = await verify.Set<Message>().SingleOrDefaultAsync(m => m.Id == $"auction-ended-{auctionId}", ct);
        Assert.NotNull(message);
        Assert.Equal("winner", message!.RecipientId);
        Assert.Equal("seller", message.SenderId);

        var outbox = await verify.OutboxMessages.SingleOrDefaultAsync(m => m.Type == "AuctionEnded", ct);
        Assert.NotNull(outbox);
        Assert.Equal(OutboxMessageStatus.Pending, outbox!.Status);
    }

    [Fact]
    public async Task Two_concurrent_sweeps_settle_each_auction_exactly_once()
    {
        await EnsureUserAsync("seller");
        await EnsureUserAsync("winner");
        var ids = new List<int>();
        for (var i = 0; i < 5; i++)
            ids.Add(await InsertEndedAuctionAsync("seller", "winner", 100m + i));

        await using var dbA = fixture.CreateDbContext();
        await using var dbB = fixture.CreateDbContext();

        // The pre-claim design would let both sweeps grab the same auction; the loser's whole batch
        // then rolled back on the auction-ended-{id} Message PK collision. SKIP LOCKED makes the
        // claims disjoint, so this must complete with no DbUpdateException.
        var ex = await Record.ExceptionAsync(() => Task.WhenAll(
            CreateJob(dbA).RunAsync(TestContext.Current.CancellationToken),
            CreateJob(dbB).RunAsync(TestContext.Current.CancellationToken)));
        Assert.Null(ex);

        await using var verify = fixture.CreateDbContext();
        var ct = TestContext.Current.CancellationToken;
        foreach (var id in ids)
        {
            var messages = await verify.Set<Message>().CountAsync(m => m.Id == $"auction-ended-{id}", ct);
            Assert.Equal(1, messages); // exactly one winner message per auction — no duplicate settlement
            Assert.NotNull((await verify.Auctions.SingleAsync(a => a.AuctionId == id, ct)).FinalizedAt);
        }
    }

    [Fact]
    public async Task Already_finalized_auction_is_not_claimed()
    {
        await EnsureUserAsync("seller");
        await EnsureUserAsync("winner");
        var auctionId = await InsertEndedAuctionAsync("seller", "winner", 1500m, finalized: true);

        await using var db = fixture.CreateDbContext();
        // The claim runs inside an explicit transaction, as its guard requires.
        await using var tx = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var claimed = await new AuctionRepository(db)
            .ClaimEndedUnfinalizedAsync(DateTimeOffset.UtcNow, batchSize: 50);
        await tx.CommitAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(claimed, a => a.AuctionId == auctionId);
    }

    // ---- helpers ----

    private async Task EnsureUserAsync(string id)
    {
        await using var db = fixture.CreateDbContext();
        if (await db.Users.AnyAsync(u => u.Id == id)) return;
        db.Users.Add(new AppUser
        {
            Id = id, DisplayName = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(),
            Email = $"{id}@test.com", NormalizedEmail = $"{id}@TEST.COM"
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> InsertEndedAuctionAsync(string sellerId, string winnerId, decimal highBid, bool finalized = false)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var auction = new Auction
        {
            ItemName = "Test Item", StartingPrice = 100m, SellerId = sellerId,
            StartTime = now.AddDays(-2), EndTime = now.AddMinutes(-5), // ended
            CurrentHighBid = highBid, CurrentHighBidderId = winnerId,
            FinalizedAt = finalized ? now.AddMinutes(-4) : null
        };
        db.Auctions.Add(auction);
        await db.SaveChangesAsync();
        return auction.AuctionId;
    }
}
