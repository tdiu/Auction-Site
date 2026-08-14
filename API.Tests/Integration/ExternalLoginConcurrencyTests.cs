using API.Core;
using API.Data;
using API.DTOs;
using API.Entities;
using API.Interfaces;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Real-Postgres coverage of the branches <c>CreateExternalUserAsync</c> can only reach under
/// contention. In the serial case Identity's <c>UserValidator</c> absorbs every collision before the
/// INSERT runs, so the savepoint rollback and the raw unique-violation catch are unreachable from a
/// unit test: EF InMemory enforces neither <c>IX_AspNetUsers_DisplayName</c> nor <c>EmailIndex</c>,
/// and there is no transaction to roll back to a savepoint.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class ExternalLoginConcurrencyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Concurrent_signups_deriving_one_name_all_succeed_with_distinct_names()
    {
        var ct = TestContext.Current.CancellationToken;
        const int signups = 8;
        var run = Guid.NewGuid().ToString("N")[..8];
        await using var provider = BuildProvider();

        var results = await Task.WhenAll(Enumerable.Range(0, signups).Select(async i =>
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAuthService>()
                .ExternalLoginAsync(
                    new ExternalLoginRequest("Google", $"{run}-sub-{i}", $"{run}-{i}@race.test", "Alex Smith"),
                    ct: ct);
        }));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));

        await using var verify = fixture.CreateDbContext();
        var names = await verify.Users
            .Where(u => u.Email!.StartsWith(run))
            .Select(u => u.DisplayName)
            .ToListAsync(ct);

        Assert.Equal(signups, names.Count);
        Assert.Equal(signups, names.Distinct().Count());
        Assert.All(names, n => Assert.StartsWith("alexsmith", n));
    }

    [Fact]
    public async Task External_signup_writes_the_login_row_with_the_account()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..8];
        await using var provider = BuildProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IAuthService>()
                .ExternalLoginAsync(
                    new ExternalLoginRequest("Google", $"{run}-sub", $"{run}@link.test", "Alex Smith"),
                    ct: ct);

            Assert.True(result.IsSuccess, result.Error);
        }

        await using var verify = fixture.CreateDbContext();
        var user = await verify.Users.SingleAsync(u => u.Email == $"{run}@link.test", ct);
        var login = await verify.UserLogins.SingleAsync(l => l.ProviderKey == $"{run}-sub", ct);

        Assert.Equal(user.Id, login.UserId);
        Assert.Equal("Google", login.LoginProvider);
    }

    [Fact]
    public async Task Concurrent_signups_for_one_email_leave_exactly_one_account()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..8];
        var email = $"{run}@dup.test";
        await using var provider = BuildProvider();

        var names = new[] { "Alpha One", "Beta Two" };
        var results = await Task.WhenAll(names.Select(async (name, i) =>
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAuthService>()
                .ExternalLoginAsync(new ExternalLoginRequest("Google", $"{run}-sub-{i}", email, name), ct: ct);
        }));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal(FailureReason.Conflict, r.Reason));

        await using var verify = fixture.CreateDbContext();
        var users = await verify.Users.Where(u => u.Email == email).ToListAsync(ct);
        var survivor = Assert.Single(users);

        var logins = await verify.UserLogins.Where(l => l.UserId == survivor.Id).ToListAsync(ct);
        Assert.Single(logins);
    }

    /// <summary>
    /// One provider, one scope per concurrent signup, so each caller holds its own AppDbContext and
    /// connection while <c>UserManager</c> and <c>IUnitOfWork</c> still share one within a scope,
    /// which is what puts both writes under the same transaction.
    /// </summary>
    private ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TokenKey"] = new string('k', 64),
                ["RefreshTokenKey"] = new string('r', 64)
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddIdentityCore<AppUser>(o =>
        {
            o.Password.RequireNonAlphanumeric = false;
            o.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AppDbContext>();

        services.AddSingleton<IConfiguration>(config);
        services.AddScoped<ITokenService, TokenService>();
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
}
