using API.Entities;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class RefreshSessionRepository(AppDbContext context) : IRefreshSessionRepository
{
    // Tracked, and includes ReplacedBy. The grace check needs the successor's state, not just the link
    public Task<RefreshSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct)
        => context.RefreshSessions
            .Include(s => s.ReplacedBy)
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

    public void Add(RefreshSession refreshSession) => context.RefreshSessions.Add(refreshSession);

    public async Task<bool> TryMarkRotatedAsync(int sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await context.RefreshSessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokedReason, SessionRevokedReason.Rotated), ct);
        return affected == 1;
    }

    public Task<RefreshSession?> ReloadAsync(int sessionId, CancellationToken ct)
        => context.RefreshSessions
            .AsNoTracking()
            .Include(s => s.ReplacedBy)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    // Separate statement rather than part of the rotate update: the predecessor is Rotated with no
    // successor until this lands, which is why the caller keeps both inside one transaction
    public Task SetReplacedByAsync(int sessionId, int successorId, CancellationToken ct)
        => context.RefreshSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.ReplacedById, successorId), ct);

    // Live rows only. Re-stamping an already-revoked row would overwrite the reason a later replay
    // reads to tell a cascade victim from the token that caused it
    public Task<int> RevokeAllForUserAsync(string userId, SessionRevokedReason reason, CancellationToken ct)
        => context.RefreshSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow)
                .SetProperty(s => s.RevokedReason, reason), ct);

    // Revoked rows are left alone until they expire so reuse detection keeps its evidence.
    // Predecessors pointing at a deleted row are handled by the FK's ON DELETE SET NULL
    public Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct)
        => context.RefreshSessions
            .Where(s => s.ExpiresAt < before)
            .ExecuteDeleteAsync(ct);
}
