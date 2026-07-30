namespace API.Entities;

public class RefreshSession
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public AppUser? User { get; set; }
    public required string TokenHash  { get; set; } // The HMAC from TokenService.HashRefreshToken
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public SessionRevokedReason? RevokedReason { get; set; }

    // Prevent grace window from serving tokens from a burned credential
    // Turns grace check from "rotated recently" into "and its successor is still live"
    // Old session points at successor, check reads session.ReplacedBy.RevokedAt == null
    // Successor is revoked (cascaded), check fails, grace window stays shut. Replay is a 401
    public int? ReplacedById { get; set; }
    public RefreshSession?  ReplacedBy { get; set; }

    public string? UserAgent { get; set; }
}
