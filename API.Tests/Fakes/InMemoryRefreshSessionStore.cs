using API.Entities;
using API.Interfaces;

namespace API.Tests.Fakes;

/// <summary>
/// Stand-in for <see cref="IRefreshSessionRepository"/>. The real one is built on ExecuteUpdate /
/// ExecuteDelete, which EF InMemory does not implement, so without this the unit tests would have to
/// substitute the repository outright and could assert nothing about the rows. This keeps the row
/// semantics that the service branches on — compare-and-set rotation, revoke-live-rows-only,
/// successor resolution — so the branching is exercised against real state.
///
/// What it deliberately does NOT model is concurrency: every call here runs to completion before the
/// next starts, so it can never produce the interleaving TryMarkRotatedAsync exists to survive. That
/// claim is a property of a Postgres row lock and is tested where it lives, in
/// <c>RefreshSessionConcurrencyTests</c>.
/// </summary>
public class InMemoryRefreshSessionStore : IRefreshSessionRepository
{
    private readonly List<RefreshSession> _rows = [];
    private int _nextId = 1;

    public IReadOnlyList<RefreshSession> Rows => _rows;

    public RefreshSession Seed(RefreshSession session)
    {
        if (session.Id == 0) session.Id = _nextId++;
        else _nextId = Math.Max(_nextId, session.Id + 1);
        _rows.Add(session);
        return session;
    }

    // The real Add leaves the Id at 0 until SaveChanges; assigning here is equivalent for the
    // service, which only reads successor.Id after awaiting CompleteAsync.
    public void Add(RefreshSession refreshSession) => Seed(refreshSession);

    public Task<RefreshSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct)
        => Task.FromResult(WithSuccessor(_rows.FirstOrDefault(s => s.TokenHash == tokenHash)));

    public Task<RefreshSession?> ReloadAsync(int sessionId, CancellationToken ct)
        => Task.FromResult(WithSuccessor(_rows.FirstOrDefault(s => s.Id == sessionId)));

    /// <summary>
    /// Runs once against the target row immediately before the next claim is evaluated, standing in
    /// for another caller's already-committed rotation. It is the only way to reach the lost-claim
    /// branch from a single-threaded test: on Postgres the loser's UPDATE blocks on the winner's row
    /// lock and then matches nothing, and this reproduces the state it wakes up to.
    /// </summary>
    public Action<RefreshSession>? BeforeNextClaim { get; set; }

    public Task<bool> TryMarkRotatedAsync(int sessionId, DateTimeOffset now, CancellationToken ct)
    {
        if (BeforeNextClaim is { } hook)
        {
            BeforeNextClaim = null;
            var target = _rows.FirstOrDefault(s => s.Id == sessionId);
            if (target is not null) hook(target);
        }

        // The WHERE RevokedAt IS NULL half of the claim. A row already rotated by someone else
        // matches nothing and the caller is told it lost.
        var row = _rows.FirstOrDefault(s => s.Id == sessionId && s.RevokedAt is null);
        if (row is null) return Task.FromResult(false);

        row.RevokedAt = now;
        row.RevokedReason = SessionRevokedReason.Rotated;
        return Task.FromResult(true);
    }

    public Task SetReplacedByAsync(int sessionId, int successorId, CancellationToken ct)
    {
        var row = _rows.FirstOrDefault(s => s.Id == sessionId);
        if (row is not null) row.ReplacedById = successorId;
        return Task.CompletedTask;
    }

    public Task<int> RevokeAllForUserAsync(string userId, SessionRevokedReason reason, CancellationToken ct)
    {
        var live = _rows.Where(s => s.UserId == userId && s.RevokedAt is null).ToList();
        foreach (var row in live)
        {
            row.RevokedAt = DateTimeOffset.UtcNow;
            row.RevokedReason = reason;
        }

        return Task.FromResult(live.Count);
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct)
        => Task.FromResult(_rows.RemoveAll(s => s.ExpiresAt < before));

    // Stands in for the Include: the grace check reads the successor's state, not just the link.
    private RefreshSession? WithSuccessor(RefreshSession? session)
    {
        if (session is not null)
            session.ReplacedBy = session.ReplacedById is null
                ? null
                : _rows.FirstOrDefault(s => s.Id == session.ReplacedById);

        return session;
    }
}
