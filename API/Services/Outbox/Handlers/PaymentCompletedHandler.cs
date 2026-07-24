using System.Text.Json;
using API.Entities;
using API.Interfaces;
using API.Services.Email.Models;

namespace API.Services.Outbox.Handlers;

public class PaymentCompletedHandler(
    IUnitOfWork unitOfWork,
    IEmailSender emailSender,
    IEmailTemplateRenderer templateRenderer,
    IConfiguration config,
    ILogger<PaymentCompletedHandler> logger) : IOutboxHandler
{
    public string Type => "PaymentCompleted";

    public async Task Handle(OutboxMessage message, CancellationToken ct)
    {
        var p = JsonSerializer.Deserialize<PaymentCompletedPayload>(message.Payload)
                ?? throw new InvalidOperationException("Malformed PaymentCompleted payload");

        // Unchanged: the seller's in-app notification, deterministic id intact.
        unitOfWork.Messages.AddMessage(new Message
        {
            Id = $"payment-completed-{p.PaymentId}",
            SenderId = p.BuyerId,
            RecipientId = p.SellerId,
            Content = $"Payment received for \"{p.ItemName}\".",
            MessageSent = DateTime.UtcNow
        });

        var payment = await unitOfWork.Payments.GetByIdAsync(p.PaymentId)
                      ?? throw new InvalidOperationException($"Payment {p.PaymentId} missing for receipt");

        var buyer = await unitOfWork.Users.GetUserByIdAsync(p.BuyerId)
                    ?? throw new InvalidOperationException($"Buyer {p.BuyerId} does not exist");

        // Deliberately NOT a throw, unlike AuctionEndedHandler. There the email is the whole
        // point of the handler; here it is one of two side effects, and a buyer with no address
        // must not cost the seller their notification on every retry until the row is reaped.
        if (string.IsNullOrEmpty(buyer.Email))
        {
            logger.LogWarning("Buyer {BuyerId} has no email; skipping receipt for payment {PaymentId}",
                p.BuyerId, p.PaymentId);
            return;
        }

        var body = await templateRenderer.RenderAsync("Receipt", new ReceiptEmailModel(
            p.PaymentId, p.ItemName, payment.Auction.Seller.DisplayName, payment.Amount,
            payment.CompletedAt ?? DateTimeOffset.UtcNow,
            $"{config["ClientAppUrl"]}/orders/{p.PaymentId}"), ct);

        // The outbox is at-least-once: a redelivered row must not send a second receipt. Same shape
        // as auction-won-{id}, which the mail layer dedupes on as the SMTP MessageId.
        await emailSender.SendAsync(
            buyer.Email,
            $"Receipt for \"{p.ItemName}\"",
            body,
            idempotencyKey: $"payment-receipt-{p.PaymentId}",
            ct);
    }
}
