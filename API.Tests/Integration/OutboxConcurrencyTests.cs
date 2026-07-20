using System.Text.Json;
using API.Data;
using API.Entities;
using API.Interfaces;
using API.Services.Outbox;
using API.Services.Outbox.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Real-Postgres coverage of the outbox claim/reap/dispatch mechanics that EF InMemory cannot
/// reproduce: the atomic SKIP LOCKED claim, the reaper's dead-letter of crash-looped rows, and the
/// unique-violation-as-delivery path against a real Message PK — the bug the lease introduced.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class OutboxConcurrencyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Concurrent_claims_are_disjoint()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearOutboxAsync(ct); // deterministic starting state — the collection shares one DB
        await SeedPendingAsync(count: 10);

        await using var dbA = fixture.CreateDbContext();
        await using var dbB = fixture.CreateDbContext();
        var lease = TimeSpan.FromMinutes(5);

        var results = await Task.WhenAll(
            new OutboxRepository(dbA).ClaimAndLeaseAsync(batchSize: 5, lease, maxAttempts: 8),
            new OutboxRepository(dbB).ClaimAndLeaseAsync(batchSize: 5, lease, maxAttempts: 8));

        var a = results[0];
        var b = results[1];
        Assert.True(a.Count <= 5 && b.Count <= 5);
        Assert.Empty(a.Intersect(b));               // no row claimed twice
        Assert.Equal(10, a.Union(b).Count());       // all 10 claimed between them

        await using var verify = fixture.CreateDbContext();
        foreach (var id in a.Union(b))
        {
            var row = await verify.OutboxMessages.SingleAsync(m => m.Id == id, ct);
            Assert.Equal(1, row.Attempts);                          // receive counted at claim
            Assert.True(row.VisibleAt > DateTimeOffset.UtcNow);     // leased into the future
        }
    }

    [Fact]
    public async Task Reaper_dead_letters_a_row_that_exhausted_its_receives()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearOutboxAsync(ct);
        const int maxAttempts = 8;
        Guid id;
        await using (var seed = fixture.CreateDbContext())
        {
            var msg = new OutboxMessage
            {
                Type = "PaymentCompleted",
                Payload = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
                VisibleAt = DateTimeOffset.UtcNow.AddMinutes(-1), // due
                Attempts = maxAttempts,                           // burned its budget, never reported a failure
                Status = OutboxMessageStatus.Pending
            };
            seed.OutboxMessages.Add(msg);
            await seed.SaveChangesAsync(ct);
            id = msg.Id;
        }

        await using (var db = fixture.CreateDbContext())
        {
            var reaped = await new OutboxRepository(db).ReapExhaustedAsync(maxAttempts);
            Assert.Equal(1, reaped);
        }

        await using var verify = fixture.CreateDbContext();
        var row = await verify.OutboxMessages.SingleAsync(m => m.Id == id, ct);
        Assert.Equal(OutboxMessageStatus.DeadLettered, row.Status);
        Assert.False(string.IsNullOrEmpty(row.LastError));
    }

    [Fact] // regression — the bug the lease introduced
    public async Task Expired_lease_double_claim_marks_the_loser_processed_not_dead_lettered()
    {
        var ct = TestContext.Current.CancellationToken;
        await ClearOutboxAsync(ct);
        await DeleteMessageAsync("payment-completed-1", ct); // a prior run may have left it
        await EnsureUserAsync("buyer");
        await EnsureUserAsync("seller");

        // One PaymentCompleted message; its handler writes Message id "payment-completed-1".
        Guid id;
        await using (var seed = fixture.CreateDbContext())
        {
            var msg = new OutboxMessage
            {
                Type = "PaymentCompleted",
                Payload = JsonSerializer.Serialize(
                    new PaymentCompletedPayload(1, 1, "buyer", "seller", "Test Item")),
                CreatedAt = DateTimeOffset.UtcNow,
                VisibleAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Status = OutboxMessageStatus.Pending
            };
            seed.OutboxMessages.Add(msg);
            await seed.SaveChangesAsync(ct);
            id = msg.Id;
        }

        var dispatcher = BuildDispatcher();

        // First delivery lands the Message and marks the row Processed.
        await dispatcher.DispatchAsync(ct);

        // Simulate the expired lease: the row is made claimable again even though its side effect
        // already landed. The re-claim's handler will collide on the Message PK.
        await using (var db = fixture.CreateDbContext())
        {
            var row = await db.OutboxMessages.SingleAsync(m => m.Id == id, ct);
            row.Status = OutboxMessageStatus.Pending;
            row.ProcessedAt = null;
            row.VisibleAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(ct);
        }

        await dispatcher.DispatchAsync(ct);

        await using var verify = fixture.CreateDbContext();
        var messages = await verify.Set<Message>().CountAsync(m => m.Id == "payment-completed-1", ct);
        Assert.Equal(1, messages);  // the collision did not create a second Message

        var final = await verify.OutboxMessages.SingleAsync(m => m.Id == id, ct);
        Assert.Equal(OutboxMessageStatus.Processed, final.Status); // delivery, NOT dead-lettered
        Assert.Null(final.LastError);                              // a duplicate key is not an error
    }

    // ---- helpers ----

    /// <summary>A dispatcher whose scopes resolve a real UnitOfWork + PaymentCompletedHandler over the fixture DB.</summary>
    private OutboxDispatcher BuildDispatcher()
    {
        var services = new ServiceCollection();
        services.AddScoped<AppDbContext>(_ => fixture.CreateDbContext());
        services.AddScoped<IUserRepository>(_ => Substitute.For<IUserRepository>());
        services.AddScoped<IBidRepository>(_ => Substitute.For<IBidRepository>());
        services.AddScoped<IAuctionRepository, AuctionRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxHandler, PaymentCompletedHandler>();
        var sp = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Outbox:BatchSize"] = "10",
                ["Outbox:MaxAttempts"] = "8",
                ["Outbox:LeaseMinutes"] = "5"
            })
            .Build();

        return new OutboxDispatcher(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new OutboxRepository(fixture.CreateDbContext()),
            config,
            NullLogger<OutboxDispatcher>.Instance);
    }

    private async Task ClearOutboxAsync(CancellationToken ct)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"OutboxMessages\"", ct);
    }

    private async Task DeleteMessageAsync(string id, CancellationToken ct)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlAsync($"DELETE FROM \"Messages\" WHERE \"Id\" = {id}", ct);
    }

    private async Task SeedPendingAsync(int count)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
            db.OutboxMessages.Add(new OutboxMessage
            {
                Type = "PaymentCompleted",
                Payload = "{}",
                CreatedAt = now,
                VisibleAt = now.AddMinutes(-1),
                Status = OutboxMessageStatus.Pending
            });
        await db.SaveChangesAsync();
    }

    private async Task EnsureUserAsync(string id)
    {
        await using var db = fixture.CreateDbContext();
        if (await db.Users.AnyAsync(u => u.Id == id)) return;
        db.Users.Add(new AppUser
        {
            Id = id,
            DisplayName = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            Email = $"{id}@test.com",
            NormalizedEmail = $"{id}@TEST.COM"
        });
        await db.SaveChangesAsync();
    }
}
