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
        if (string.IsNullOrEmpty(winner.Email))
            throw new InvalidOperationException($"Winner {payload.WinnerId} has no email");

        var url = $"{config["ClientAppUrl"]}/auctions/{payload.AuctionId}";

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
