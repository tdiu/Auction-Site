using API.Entities;
using API.Interfaces;
using API.Services.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;

namespace API.Tests.Services;

/// <summary>
/// Fast coverage of the dispatcher's per-message state machine. The claim is raw SQL, so these
/// substitute <see cref="IOutboxRepository"/> rather than reach for EF InMemory — but the scope and
/// handler-resolution path (<c>CreateScope()</c> → <c>GetServices&lt;IOutboxHandler&gt;()</c>) runs for
/// real, driven by a genuine <see cref="ServiceProvider"/> holding the substitutes.
/// </summary>
public class OutboxDispatcherTests
{
    /// <summary>An IOutboxHandler for a given Type whose Handle throws whatever it is told to.</summary>
    private sealed class StubHandler(string type, Func<Task> behavior) : IOutboxHandler
    {
        public string Type => type;
        public Task Handle(OutboxMessage message, CancellationToken ct) => behavior();
    }

    /// <summary>
    /// Builds a dispatcher whose scopes resolve <paramref name="uow"/> and <paramref name="handler"/>,
    /// and whose claim/reap repository is <paramref name="repo"/>. Registered as singletons so every
    /// scope (process, then the fresh failure/mark scope) sees the same substitutes and their mutations.
    /// </summary>
    private static OutboxDispatcher Build(IUnitOfWork uow, IOutboxRepository repo, IOutboxHandler handler, int maxAttempts)
    {
        var services = new ServiceCollection();
        services.AddSingleton(uow);
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Outbox:BatchSize"] = "10",
                ["Outbox:MaxAttempts"] = maxAttempts.ToString(),
                ["Outbox:LeaseMinutes"] = "5"
            })
            .Build();

        return new OutboxDispatcher(
            sp.GetRequiredService<IServiceScopeFactory>(), repo, config, NullLogger<OutboxDispatcher>.Instance);
    }

    /// <summary>Wires a single claimed message: claim returns its id, the scoped repo reloads it.</summary>
    private static (IUnitOfWork uow, IOutboxRepository repo) WireSingle(OutboxMessage msg)
    {
        var repo = Substitute.For<IOutboxRepository>();
        repo.ClaimAndLeaseAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<int>()).Returns([msg.Id]);
        repo.GetAsync(msg.Id).Returns(msg);

        var uow = Substitute.For<IUnitOfWork>();
        uow.Outbox.Returns(repo);
        uow.CompleteAsync().Returns(true);
        return (uow, repo);
    }

    private static StubHandler Throwing(string type, Exception ex) =>
        new(type, () => Task.FromException(ex));

    [Fact]
    public async Task Backs_off_and_stays_pending_when_handler_throws()
    {
        var msg = new OutboxMessage
        {
            Type = "PaymentCompleted",
            Payload = "{}",
            CreatedAt = default,
            VisibleAt = default,
            Attempts = 1
        };
        var (uow, repo) = WireSingle(msg);
        var dispatcher = Build(uow, repo, Throwing("PaymentCompleted", new Exception("boom")), maxAttempts: 8);

        await dispatcher.DispatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OutboxMessageStatus.Pending, msg.Status);  // still claimable
        Assert.Null(msg.ProcessedAt);                           // never set on a failure path
        Assert.NotNull(msg.LastError);
        Assert.True(msg.VisibleAt > DateTimeOffset.UtcNow);     // backed off, not retried immediately
    }

    [Fact]
    public async Task Dead_letters_when_attempts_reach_max()
    {
        var msg = new OutboxMessage
        {
            Type = "PaymentCompleted",
            Payload = "{}",
            CreatedAt = default,
            VisibleAt = default,
            Attempts = 3
        };
        var (uow, repo) = WireSingle(msg);
        var dispatcher = Build(uow, repo, Throwing("PaymentCompleted", new Exception("boom")), maxAttempts: 3);

        await dispatcher.DispatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OutboxMessageStatus.DeadLettered, msg.Status);
        Assert.Null(msg.ProcessedAt);       // dead-lettered is a give-up, not a delivery
        Assert.NotNull(msg.LastError);
    }

    [Fact]
    public async Task Treats_unique_violation_as_delivery_and_marks_processed()
    {
        var msg = new OutboxMessage
        {
            Type = "PaymentCompleted",
            Payload = "{}",
            CreatedAt = default,
            VisibleAt = default,
            Attempts = 1
        };
        var (uow, repo) = WireSingle(msg);
        // A deterministic-id collision surfaces as DbUpdateException/23505: proof a prior claim
        // already delivered this message, so it is delivery — not failure.
        var unique = new DbUpdateException("dup",
            new PostgresException("duplicate key value", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
        var dispatcher = Build(uow, repo, Throwing("PaymentCompleted", unique), maxAttempts: 8);

        await dispatcher.DispatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OutboxMessageStatus.Processed, msg.Status);
        Assert.NotNull(msg.ProcessedAt);
        Assert.Null(msg.LastError);         // a duplicate key is not an error
    }

    [Fact]
    public async Task Leaves_an_already_processed_row_untouched_when_a_late_failure_arrives()
    {
        // Simulates the expired-lease race: this dispatcher's handler throws, but by the time its
        // fresh failure scope reloads the row, another dispatcher has already delivered and marked it
        // Processed. The status guard in RecordFailureAsync must not stomp attempts/error onto it.
        var claimed = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "PaymentCompleted",
            Payload = "{}",
            CreatedAt = default,
            VisibleAt = default,
            Attempts = 1
        };
        var reloadedAsProcessed = new OutboxMessage
        {
            Id = claimed.Id,
            Type = "PaymentCompleted",
            Payload = "{}",
            CreatedAt = default,
            VisibleAt = default,
            Attempts = 1,
            Status = OutboxMessageStatus.Processed,
            ProcessedAt = DateTimeOffset.UtcNow,
            LastError = null
        };

        var repo = Substitute.For<IOutboxRepository>();
        repo.ClaimAndLeaseAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<int>()).Returns([claimed.Id]);
        // First load (ProcessAsync) sees it Pending; second load (RecordFailureAsync) sees it Processed.
        repo.GetAsync(claimed.Id).Returns(claimed, reloadedAsProcessed);
        var uow = Substitute.For<IUnitOfWork>();
        uow.Outbox.Returns(repo);
        uow.CompleteAsync().Returns(true);

        var dispatcher = Build(uow, repo, Throwing("PaymentCompleted", new Exception("late failure")), maxAttempts: 8);

        await dispatcher.DispatchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OutboxMessageStatus.Processed, reloadedAsProcessed.Status); // unchanged
        Assert.Null(reloadedAsProcessed.LastError);                              // not stomped
    }
}
