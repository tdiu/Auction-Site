using System.Text.Json;
using API.Entities;
using API.Interfaces;
using API.Services.Email.Models;
using API.Services.Outbox;
using API.Services.Outbox.Handlers;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace API.Tests.Services.Outbox;

/// <summary>
/// The delivery side of the auction-ended flow. The sweeper tests prove the AuctionEnded row is
/// enqueued; these prove the handler resolves the winner, renders the Winner view, and sends with a
/// deterministic idempotency key — plus the throw branches the dispatcher relies on for retry.
/// </summary>
public class AuctionEndedHandlerTests
{
    private const string ClientAppUrl = "https://client.test";

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly IEmailTemplateRenderer _renderer = Substitute.For<IEmailTemplateRenderer>();

    public AuctionEndedHandlerTests() => _uow.Users.Returns(_users);

    private AuctionEndedHandler CreateHandler() =>
        new(_uow, _email, _renderer, Config());

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ClientAppUrl"] = ClientAppUrl })
            .Build();

    private static OutboxMessage MessageFor(AuctionEndedPayload payload) => new()
    {
        Type = "AuctionEnded", CreatedAt = default, VisibleAt = default,
        Payload = JsonSerializer.Serialize(payload)
    };

    [Fact]
    public async Task Renders_winner_view_and_sends_with_deterministic_idempotency_key()
    {
        _users.GetUserByIdAsync("winner")
            .Returns(new AppUser { Id = "winner", DisplayName = "Winner", Email = "w@x.io" });
        _renderer.RenderAsync("Winner", Arg.Any<WinnerEmailModel>(), Arg.Any<CancellationToken>())
            .Returns("<p>won</p>");

        var msg = MessageFor(new AuctionEndedPayload(42, "winner", "Strat", 1500m));

        await CreateHandler().Handle(msg, TestContext.Current.CancellationToken);

        // The URL and model handed to the view come from the payload + config.
        await _renderer.Received(1).RenderAsync("Winner",
            Arg.Is<WinnerEmailModel>(m =>
                m.ItemName == "Strat" && m.Amount == 1500m &&
                m.AuctionUrl == "https://client.test/auctions/42"),
            Arg.Any<CancellationToken>());

        // The idempotency key is the load-bearing assertion: it is the SMTP MessageId the mail layer
        // dedupes on, and the same `auction-won-{id}` shape the dispatcher reads as delivery on a 23505.
        await _email.Received(1).SendAsync("w@x.io", "You won \"Strat\"", "<p>won</p>",
            "auction-won-42", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Throws_and_does_not_send_when_winner_has_no_email(string? email)
    {
        _users.GetUserByIdAsync("winner")
            .Returns(new AppUser { Id = "winner", DisplayName = "Winner", Email = email });

        var msg = MessageFor(new AuctionEndedPayload(42, "winner", "Strat", 1500m));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(msg, TestContext.Current.CancellationToken));

        // Throwing (not silently skipping) is what routes the row onto the dispatcher's backoff path.
        await _email.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_when_winner_does_not_exist()
    {
        _users.GetUserByIdAsync("winner").Returns((AppUser?)null);

        var msg = MessageFor(new AuctionEndedPayload(42, "winner", "Strat", 1500m));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(msg, TestContext.Current.CancellationToken));
        await _email.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_on_malformed_payload_before_any_send()
    {
        // JSON literal "null" deserializes to a null payload — the handler's guard rejects it.
        var msg = new OutboxMessage { Type = "AuctionEnded", Payload = "null", CreatedAt = default, VisibleAt = default };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(msg, TestContext.Current.CancellationToken));
        await _email.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
