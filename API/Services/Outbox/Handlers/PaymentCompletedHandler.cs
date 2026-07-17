using System.Text.Json;
using API.Entities;
using API.Interfaces;

namespace API.Services.Outbox.Handlers;

public class PaymentCompletedHandler(IUnitOfWork unitOfWork) : IOutboxHandler
{
    public string Type => "PaymentCompleted";

    public Task Handle(OutboxMessage message, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<PaymentCompletedPayload>(message.Payload)
                ?? throw new InvalidOperationException("Malformed PaymentCompleted payload");

        unitOfWork.Messages.AddMessage(new Message
        {
            Id = $"payment-completed-{p.PaymentId}",
            SenderId = p.BuyerId,
            RecipientId = p.SellerId,
            Content = $"Payment received for \"{p.ItemName}\".",
            MessageSent = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }
}
