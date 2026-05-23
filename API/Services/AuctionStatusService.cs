using API.Interfaces;

namespace API.Services;

public class AuctionStatusService(IServiceScopeFactory scopeFactory, ILogger<AuctionStatusService> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval);

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IAuctionRepository>();

                var updated = await repo.UpdateAuctionStatusesAsync();

                if (updated > 0)
                    logger.LogInformation("Auction status scan: {Count} auctions updated", updated);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error updating auction statuses");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}