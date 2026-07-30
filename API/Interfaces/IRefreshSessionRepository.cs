using API.Entities;

namespace API.Interfaces;

public interface IRefreshSessionRepository
{
    Task<RefreshSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct);
    Task<RefreshSession?> ReloadAsync(int sessionId, CancellationToken ct);
    void Add(RefreshSession refreshSession);
    Task<bool> TryMarkRotatedAsync(int sessionId, DateTimeOffset now, CancellationToken ct);
    Task SetReplacedByAsync(int sessionId, int successorId, CancellationToken ct);
    Task<int> RevokeAllForUserAsync(string userId, SessionRevokedReason reason, CancellationToken ct);
    Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct);
}
