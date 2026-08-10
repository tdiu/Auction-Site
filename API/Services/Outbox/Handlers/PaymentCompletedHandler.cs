using System.Text.Json;
using API.Entities;
using API.Interfaces;

namespace API.Services.Outbox.Handlers;

/// <summary>
/// Seller-side half of a completed payment: the in-app "payment received" notification, and nothing
/// else. The buyer's receipt is <see cref="PaymentReceiptHandler"/>, on its own outbox row, so a mail
/// outage retries only the receipt instead of dead-lettering this notification alongside it.
/// </summary>
public class PaymentCompletedHandler(IUnitOfWork unitOfWork) : IOutboxHandler
{
    public string Type => "PaymentCompleted";

    public Task Handle(OutboxMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PaymentCompletedPayload>(message.Payload)
            ?? throw new InvalidOperationException("Malformed paymentcompleted payload");

        unitOfWork.Messages.AddMessage(new Message
        {
            Id = $"payment-completed-{payload.PaymentId}",
            SenderId = payload.BuyerId,
            RecipientId = payload.SellerId,
            Content = $"payment received for \"{payload.ItemName}\".",
            MessageSent = DateTimeOffset.UtcNow
        });

        return Task.CompletedTask;
    }
}
