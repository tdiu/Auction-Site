using API.Entities;
using API.Interfaces;

namespace API.Services;

public class AuctionSettleJob(IUnitOfWork unitOfWork, IConfiguration config, ILogger logger)
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
                    Id = $"auctioon-ended-{auction.AuctionId}",
                    SenderId = auction.SellerId,
                    RecipientId = auction.CurrentHighBidderId,
                    Content = $"You won \"{auction.ItemName}\" for {auction.CurrentHighBid:C}. Click to pay.",
                    MessageSent = now
                });
            }
        }

        await unitOfWork.CompleteAsync();
        await tx.CommitAsync(ct);
        logger.LogInformation("Settlement swept {Count} ended auctions",  auctions.Count);
    }
}
