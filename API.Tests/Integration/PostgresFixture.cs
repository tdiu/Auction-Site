using API.Data;
using API.Interfaces;
using API.Services;
using API.Tests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Stripe;
using Testcontainers.PostgreSql;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Boots a throwaway <c>postgres:16</c> container once per collection and applies the real EF
/// migrations, so the Postgres-specific unique indexes (which EF InMemory cannot enforce) exist.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>A real UnitOfWork + PaymentService over the given context, with a fake Stripe backend.</summary>
    public PaymentService CreatePaymentService(AppDbContext db, IHttpClient stripeHttp)
    {
        var unitOfWork = new UnitOfWork(
            db,
            Substitute.For<IUserRepository>(),
            new AuctionRepository(db),
            Substitute.For<IBidRepository>(),
            new PaymentRepository(db));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientAppUrl"] = PaymentTestContext.ClientAppUrl,
                ["Stripe:WebhookSecret"] = PaymentTestContext.WebhookSecret
            })
            .Build();

        var stripe = new StripeClient("sk_test_123", httpClient: stripeHttp);
        return new PaymentService(unitOfWork, config, stripe, NullLogger<PaymentService>.Instance);
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
