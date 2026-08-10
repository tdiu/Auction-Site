using System.Text.Json;
using API.Entities;
using API.Interfaces;
using API.Services.Email.Models;

namespace API.Services.Outbox.Handlers;

/// <summary>
/// Buyer-side half of a completed payment: the receipt email, and nothing else. Split from
/// <see cref="PaymentCompletedHandler"/> so the two side effects get independent retry state. It is
/// free to throw on any failure precisely because it owns no DB write that something else depends on.
/// Delivery is at-least-once, guarded by the idempotency key rather than by a deterministic id:
/// the dispatcher's 23505-as-delivery rule cannot help here, since the send happens before the commit.
/// </summary>
public class PaymentReceiptHandler(
    IUnitOfWork unitOfWork,
    IEmailSender emailSender,
    IEmailTemplateRenderer templateRenderer,
    IConfiguration config) : IOutboxHandler
{
    public string Type => "PaymentReceipt";

    public async Task Handle(OutboxMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PaymentCompletedPayload>(message.Payload)
            ?? throw new InvalidOperationException("Malformed paymentreceipt payload");

        var payment = await unitOfWork.Payments.GetByIdAsync(payload.PaymentId)
            ?? throw new InvalidOperationException($"Payment {payload.PaymentId} not found");

        var buyer = await unitOfWork.Users.GetUserByIdAsync(payload.BuyerId)
            ?? throw new InvalidOperationException($"Buyer {payload.BuyerId} not found");

        // Unreachable while RequireUniqueEmail is set: Identity's UserValidator rejects a null or
        // empty email on both CreateAsync and UpdateAsync. Kept as an assertion because the nullable
        // type is inherited from IdentityUser and cannot express that constraint.
        if (string.IsNullOrEmpty(buyer.Email))
            throw new InvalidOperationException($"Buyer {payload.BuyerId} email not set");

        var body = await templateRenderer.RenderAsync("Receipt", new ReceiptEmailModel(
            payload.PaymentId, payload.ItemName, payment.Auction.Seller.DisplayName, payment.Amount,
            payment.CompletedAt ?? DateTimeOffset.UtcNow,
            $"{config["ClientAppUrl"]}/orders/{payload.PaymentId}"), ct);

        await emailSender.SendAsync(buyer.Email, $"Receipt for \"{payload.ItemName}\"", body,
            idempotencyKey: $"payment-receipt-{payload.PaymentId}", ct);
    }
}
