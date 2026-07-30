using API.Interfaces;

namespace API.Services;

public class SessionSweepJob(IUnitOfWork unitOfWork, ILogger<SessionSweepJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        // Rows live until their original ExpiresAt, revoked or not.
        // Deleting a rotated row early means a replayed token reads as "unknown" instead of "reuse"
        var deleted = await unitOfWork.RefreshSessions.DeleteExpiredAsync(DateTimeOffset.UtcNow.AddDays(-1), ct);

        if (deleted > 0)
            logger.LogInformation("Swept {Count} expired refresh sessions", deleted);
    }
}
