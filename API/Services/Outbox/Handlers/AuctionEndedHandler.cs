using System.Text.Json;
using API.Entities;
using API.Interfaces;
using API.Services.Email.Models;

namespace API.Services.Outbox.Handlers;

public class AuctionEndedHandler(
    IUnitOfWork unitOfWork,
    IEmailSender emailSender,
    IEmailTemplateRenderer templateRenderer,
    IConfiguration config) : IOutboxHandler
{
    public string Type => "AuctionEnded";

    public async Task Handle(OutboxMessage outboxMessage, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<AuctionEndedPayload>(outboxMessage.Payload)
                      ?? throw new InvalidOperationException("Malformed AuctionEnded payload");

        var winner = await unitOfWork.Users.GetUserByIdAsync(payload.WinnerId)
            ?? throw new InvalidOperationException($"Winner {payload.WinnerId} does not exist");

        // A Buy Now buyer who already paid shouldn't get an email asking for payment
        // Correctness guarantee handled in CreateCheckoutSession
        var payment = await unitOfWork.Payments.GetByAuctionIdAsync(payload.AuctionId);
        if (payment is { Status: PaymentStatus.Paid })
            return;

        if (string.IsNullOrEmpty(winner.Email))
            throw new InvalidOperationException($"Winner {payload.WinnerId} has no email");

        // Deep-link opens pay panel on arrival. User still clicks Pay themselves
        var url = $"{config["ClientAppUrl"]}/auctions/{payload.AuctionId}?pay=1";

        var body = await templateRenderer.RenderAsync("Winner",
            new WinnerEmailModel(payload.ItemName, payload.Amount, url), cancellationToken);

        await emailSender.SendAsync(
            winner.Email,
            $"You won \"{payload.ItemName}\"",
            body,
            idempotencyKey: $"auction-won-{payload.AuctionId}",
            cancellationToken);
    }
}
