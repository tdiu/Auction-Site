using System.Text.Json;
using API.Entities;
using API.Interfaces;
using API.Services.Outbox;
using API.Services.Outbox.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace API.Tests.Services.Outbox;

/// <summary>
/// Fast coverage of the payment-completed handler. It has two side effects: the seller's in-app
/// Message (deterministic id, the dispatcher's 23505-as-delivery guard depends on it) and the
/// buyer's receipt email (idempotency-keyed, and skipped rather than thrown when the buyer has no
/// address so a redelivery can't cost the seller their notification).
/// </summary>
public class PaymentCompletedHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IEmailTemplateRenderer _renderer = Substitute.For<IEmailTemplateRenderer>();

    public PaymentCompletedHandlerTests()
    {
        _uow.Messages.Returns(_messages);
        _uow.Payments.Returns(_payments);
        _uow.Users.Returns(_users);
        _renderer.RenderAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns("<html>receipt</html>");
    }

    private PaymentCompletedHandler CreateHandler()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ClientAppUrl"] = "https://client.test" })
            .Build();
        return new PaymentCompletedHandler(_uow, _emailSender, _renderer, config,
            Substitute.For<ILogger<PaymentCompletedHandler>>());
    }

    private static OutboxMessage MessageFor(PaymentCompletedPayload payload) => new()
    {
        Type = "PaymentCompleted",
        CreatedAt = default,
        VisibleAt = default,
        Payload = JsonSerializer.Serialize(payload)
    };

    private static Payment OrderPayment(int paymentId = 7, string seller = "Seller Co") => new()
    {
        AuctionId = 1,
        UserId = "buyer",
        Amount = 150m,
        Status = PaymentStatus.Paid,
        CompletedAt = DateTimeOffset.UtcNow,
        Auction = new Auction
        {
            AuctionId = 1,
            ItemName = "Strat",
            StartingPrice = 100m,
            SellerId = "seller",
            StartTime = DateTimeOffset.UtcNow.AddDays(-2),
            EndTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            Seller = new AppUser { DisplayName = seller }
        }
    };

    private void SetUp(PaymentCompletedPayload payload, Payment? payment = null, string? buyerEmail = "buyer@test.com")
    {
        _payments.GetByIdAsync(payload.PaymentId).Returns(payment ?? OrderPayment(payload.PaymentId));
        _users.GetUserByIdAsync(payload.BuyerId)
            .Returns(new AppUser { DisplayName = "Buyer", Email = buyerEmail });
    }

    [Fact]
    public async Task Stages_message_with_deterministic_id_from_buyer_to_seller()
    {
        var payload = new PaymentCompletedPayload(
            PaymentId: 7, AuctionId: 1, BuyerId: "buyer", SellerId: "seller", ItemName: "Strat");
        SetUp(payload);

        await CreateHandler().Handle(MessageFor(payload), TestContext.Current.CancellationToken);

        // The deterministic id is load-bearing: it is what the dispatcher reads a unique violation
        // against to conclude "already delivered" rather than "failed".
        _messages.Received(1).AddMessage(Arg.Is<Message>(m =>
            m.Id == "payment-completed-7" &&
            m.SenderId == "buyer" && m.RecipientId == "seller" &&
            m.Content.Contains("Strat")));
    }

    [Fact]
    public async Task Sends_receipt_to_buyer_with_idempotency_key()
    {
        var payload = new PaymentCompletedPayload(
            PaymentId: 7, AuctionId: 1, BuyerId: "buyer", SellerId: "seller", ItemName: "Strat");
        SetUp(payload);

        await CreateHandler().Handle(MessageFor(payload), TestContext.Current.CancellationToken);

        await _emailSender.Received(1).SendAsync(
            "buyer@test.com",
            Arg.Is<string>(s => s.Contains("Strat")),
            Arg.Any<string>(),
            "payment-receipt-7",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_receipt_but_still_stages_message_when_buyer_has_no_email()
    {
        var payload = new PaymentCompletedPayload(
            PaymentId: 7, AuctionId: 1, BuyerId: "buyer", SellerId: "seller", ItemName: "Strat");
        SetUp(payload, buyerEmail: null);

        await CreateHandler().Handle(MessageFor(payload), TestContext.Current.CancellationToken);

        // The seller's notification survives a buyer with no address; only the receipt is skipped.
        _messages.Received(1).AddMessage(Arg.Any<Message>());
        await _emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_on_malformed_payload_and_stages_nothing()
    {
        // JSON literal "null" deserializes to a null payload; the handler's guard rejects it.
        var msg = new OutboxMessage { Type = "PaymentCompleted", Payload = "null", CreatedAt = default, VisibleAt = default };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(msg, TestContext.Current.CancellationToken));
        _messages.DidNotReceive().AddMessage(Arg.Any<Message>());
    }
}
