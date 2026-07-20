using System.Text.Json;
using API.Entities;
using API.Interfaces;
using API.Services.Outbox;

namespace API.Services;

public class AuctionSettlementJob(IUnitOfWork unitOfWork, IConfiguration config, ILogger<AuctionSettlementJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var batchSize = config.GetValue("Settlement:BatchSize", 50);
        var now =  DateTimeOffset.UtcNow;

        await using var tx = await unitOfWork.BeginTransactionAsync(ct);

        var auctions = await unitOfWork.Auctions.ClaimEndedUnfinalizedAsync(now, batchSize);
        if (auctions.Count == 0)
        {
            await tx.CommitAsync(ct);
            return;
        }

        foreach (var auction in auctions)
        {
            auction.Finalize(now);

            if (auction is { CurrentHighBid: not null, CurrentHighBidderId : not null })
            {
                unitOfWork.Messages.AddMessage(new Message
                {
                    Id = $"auction-ended-{auction.AuctionId}",
                    SenderId = auction.SellerId,
                    RecipientId = auction.CurrentHighBidderId,
                    Content = $"You won \"{auction.ItemName}\" for {auction.CurrentHighBid:C}. Click to pay.",
                    MessageSent = now
                });

                unitOfWork.Outbox.Add(new OutboxMessage
                {
                    Type = "AuctionEnded",
                    CreatedAt = now,
                    VisibleAt = now,
                    Payload = JsonSerializer.Serialize(new AuctionEndedPayload(
                        auction.AuctionId, auction.CurrentHighBidderId, auction.ItemName, auction.CurrentHighBid.Value))
                });
            }
        }

        await unitOfWork.CompleteAsync();
        await tx.CommitAsync(ct);
        logger.LogInformation("Settlement swept {Count} ended auctions",  auctions.Count);
    }
}
