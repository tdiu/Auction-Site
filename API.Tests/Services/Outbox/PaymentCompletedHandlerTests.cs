using System.Text.Json;
using API.Entities;
using API.Interfaces;
using API.Services.Outbox;
using API.Services.Outbox.Handlers;
using NSubstitute;
using Xunit;

namespace API.Tests.Services.Outbox;

/// <summary>
/// Fast coverage of the payment-completed handler. Its one side effect is the seller's in-app
/// Message, written under a deterministic id because the dispatcher's 23505-as-delivery guard
/// depends on it. The buyer's receipt lives on a separate outbox row; see
/// <see cref="PaymentReceiptHandlerTests"/>.
/// </summary>
public class PaymentCompletedHandlerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();

    public PaymentCompletedHandlerTests() => _uow.Messages.Returns(_messages);

    private PaymentCompletedHandler CreateHandler() => new(_uow);

    private static OutboxMessage MessageFor(PaymentCompletedPayload payload) => new()
    {
        Type = "PaymentCompleted",
        CreatedAt = default,
        VisibleAt = default,
        Payload = JsonSerializer.Serialize(payload)
    };

    private static PaymentCompletedPayload Payload() => new(
        PaymentId: 7, AuctionId: 1, BuyerId: "buyer", SellerId: "seller", ItemName: "Strat");

    [Fact]
    public async Task Stages_message_with_deterministic_id_from_buyer_to_seller()
    {
        var payload = Payload();

        await CreateHandler().Handle(MessageFor(payload), TestContext.Current.CancellationToken);

        // The deterministic id is load-bearing: it is what the dispatcher reads a unique violation
        // against to conclude "already delivered" rather than "failed".
        _messages.Received(1).AddMessage(Arg.Is<Message>(m =>
            m.Id == "payment-completed-7" &&
            m.SenderId == "buyer" && m.RecipientId == "seller" &&
            m.Content.Contains("Strat")));
    }

    [Fact]
    public async Task Stages_the_write_without_committing_it()
    {
        // The dispatcher owns the commit so it can co-commit the Message with Status = Processed.
        // A handler that saved for itself would break that atomicity, so assert it never does.
        await CreateHandler().Handle(MessageFor(Payload()), TestContext.Current.CancellationToken);

        await _uow.DidNotReceive().CompleteAsync();
    }

    [Fact]
    public async Task Throws_on_malformed_payload_and_stages_nothing()
    {
        // JSON literal "null" deserializes to a null payload; the handler's guard rejects it.
        var msg = new OutboxMessage
        {
            Type = "PaymentCompleted",
            Payload = "null",
            CreatedAt = default,
            VisibleAt = default
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(msg, TestContext.Current.CancellationToken));
        _messages.DidNotReceive().AddMessage(Arg.Any<Message>());
    }
}
