using API.Data;
using API.Entities;
using API.Interfaces;
using API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Real-Postgres coverage of the one claim the unit tests cannot make. Rotation is a read followed
/// by a write, and the read is not a lock; what makes it safe is that the claiming UPDATE carries
/// <c>WHERE "RevokedAt" IS NULL</c>, so a concurrent caller blocks on the row lock and then
/// re-evaluates the predicate against the winner's committed version. That is a property of the
/// database, not of the C#, and the in-memory store used by <c>AuthServiceTests</c> can never
/// exhibit it — every call there runs to completion before the next begins.
///
/// The scenario is not hypothetical: <c>provideAppInitializer</c> makes every tab boot call
/// refresh-token, and a restored window fires them all against one shared cookie.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class RefreshSessionConcurrencyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Only_one_of_two_concurrent_claims_on_the_same_session_wins()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await EnsureUserAsync("claim-race");
        await ClearSessionsAsync(userId, ct);
        var sessionId = await SeedSessionAsync(userId, "hashed-claim-token", ct);

        // Separate contexts, so these are two connections racing rather than one serialised by EF.
        await using var dbA = fixture.CreateDbContext();
        await using var dbB = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;

        var results = await Task.WhenAll(
            new RefreshSessionRepository(dbA).TryMarkRotatedAsync(sessionId, now, ct),
            new RefreshSessionRepository(dbB).TryMarkRotatedAsync(sessionId, now, ct));

        Assert.Equal(1, results.Count(won => won));   // exactly one claim, however many present the token

        await using var verify = fixture.CreateDbContext();
        var row = await verify.RefreshSessions.SingleAsync(s => s.Id == sessionId, ct);
        Assert.Equal(SessionRevokedReason.Rotated, row.RevokedReason);
    }

    [Fact]
    public async Task Concurrent_refresh_of_one_token_issues_exactly_one_successor()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await EnsureUserAsync("refresh-race");
        await ClearSessionsAsync(userId, ct);
        var predecessorId = await SeedSessionAsync(userId, "hashed-shared-token", ct);

        await using var providerA = BuildAuthStack("tab-a");
        await using var providerB = BuildAuthStack("tab-b");
        using var scopeA = providerA.CreateScope();
        using var scopeB = providerB.CreateScope();

        var results = await Task.WhenAll(
            scopeA.ServiceProvider.GetRequiredService<IAuthService>()
                .RefreshTokenAsync("shared-token", "tab-a", ct),
            scopeB.ServiceProvider.GetRequiredService<IAuthService>()
                .RefreshTokenAsync("shared-token", "tab-b", ct));

        // Both tabs are served — losing the race is a tab restore, not an attack, so neither user
        // is signed out. This is the behaviour the grace window exists to produce.
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.All(results, r => Assert.Equal("access-token", r.Value!.User.Token));

        // But only one of them was given a refresh token: one live chain, not two.
        Assert.Equal(1, results.Count(r => r.Value!.RefreshToken is not null));

        await using var verify = fixture.CreateDbContext();
        var rows = await verify.RefreshSessions
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        Assert.Equal(2, rows.Count);   // the predecessor and exactly one successor

        var predecessor = rows.Single(s => s.Id == predecessorId);
        var successor = rows.Single(s => s.Id != predecessorId);

        // Superseded rather than orphaned: the link is what lets the grace check tell a racing tab
        // from a replay, and it is committed with the rotation rather than after it.
        Assert.Equal(SessionRevokedReason.Rotated, predecessor.RevokedReason);
        Assert.Equal(successor.Id, predecessor.ReplacedById);
        Assert.Null(successor.RevokedAt);
    }

    [Fact] // the reason the self-FK is ON DELETE SET NULL rather than RESTRICT
    public async Task Sweeping_an_expired_successor_nulls_the_link_instead_of_blocking()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await EnsureUserAsync("sweep-link");
        await ClearSessionsAsync(userId, ct);

        var successorId = await SeedSessionAsync(
            userId, "hashed-dead-successor", ct, expiresAt: DateTimeOffset.UtcNow.AddDays(-2));
        var predecessorId = await SeedSessionAsync(
            userId, "hashed-live-predecessor", ct, expiresAt: DateTimeOffset.UtcNow.AddDays(7));

        await using (var link = fixture.CreateDbContext())
        {
            var row = await link.RefreshSessions.SingleAsync(s => s.Id == predecessorId, ct);
            row.ReplacedById = successorId;
            await link.SaveChangesAsync(ct);
        }

        await using (var sweep = fixture.CreateDbContext())
        {
            var deleted = await new RefreshSessionRepository(sweep)
                .DeleteExpiredAsync(DateTimeOffset.UtcNow.AddDays(-1), ct);
            Assert.Equal(1, deleted);   // the bulk delete is not blocked by the inbound reference
        }

        await using var verify = fixture.CreateDbContext();
        Assert.False(await verify.RefreshSessions.AnyAsync(s => s.Id == successorId, ct));

        var predecessor = await verify.RefreshSessions.SingleAsync(s => s.Id == predecessorId, ct);
        Assert.Null(predecessor.ReplacedById);   // losing the link on a dead row costs nothing
    }

    // ---- helpers ----

    /// <summary>
    /// A real UnitOfWork + RefreshSessionRepository + AuthService over the fixture database. Each
    /// call gets its own provider so the two racers hold independent DbContexts and connections;
    /// only the session repository is real, since that is the only one AuthService touches.
    /// </summary>
    private ServiceProvider BuildAuthStack(string tokenPrefix)
    {
        var tokenService = Substitute.For<ITokenService>();
        tokenService.CreateToken(Arg.Any<AppUser>()).Returns("access-token");
        tokenService.HashRefreshToken(Arg.Any<string>()).Returns(ci => $"hashed-{ci.Arg<string>()}");

        // Distinct per racer: two successors minted with the same hash would collide on the unique
        // index and mask the thing under test with a duplicate-key error.
        var issued = 0;
        tokenService.GenerateRefreshToken().Returns(_ => $"{tokenPrefix}-{++issued}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddIdentityCore<AppUser>(o =>
        {
            o.Password.RequireNonAlphanumeric = false;
            o.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped(_ => tokenService);
        services.AddScoped<IUserRepository>(_ => Substitute.For<IUserRepository>());
        services.AddScoped<IAuctionRepository>(_ => Substitute.For<IAuctionRepository>());
        services.AddScoped<IBidRepository>(_ => Substitute.For<IBidRepository>());
        services.AddScoped<IPaymentRepository>(_ => Substitute.For<IPaymentRepository>());
        services.AddScoped<IMessageRepository>(_ => Substitute.For<IMessageRepository>());
        services.AddScoped<IOutboxRepository>(_ => Substitute.For<IOutboxRepository>());
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuthService, AuthService>();

        return services.BuildServiceProvider();
    }

    private async Task<string> EnsureUserAsync(string id)
    {
        await using var db = fixture.CreateDbContext();
        if (!await db.Users.AnyAsync(u => u.Id == id))
        {
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

        return id;
    }

    // The collection shares one database, so each test starts from a known set of rows for its user.
    private async Task ClearSessionsAsync(string userId, CancellationToken ct)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlAsync(
            $"""UPDATE "RefreshSessions" SET "ReplacedById" = NULL WHERE "UserId" = {userId}""", ct);
        await db.Database.ExecuteSqlAsync(
            $"""DELETE FROM "RefreshSessions" WHERE "UserId" = {userId}""", ct);
    }

    private async Task<int> SeedSessionAsync(
        string userId, string tokenHash, CancellationToken ct, DateTimeOffset? expiresAt = null)
    {
        await using var db = fixture.CreateDbContext();
        var session = new RefreshSession
        {
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7)
        };
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.Id;
    }
}
