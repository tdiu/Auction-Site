using System.Security.Claims;

namespace API.Services;

public readonly record struct RateLimitBucket(string Key, int PermitLimit);

/// <summary>
/// Chooses which bucket a request counts against, and how big that bucket is. Everything sharing a
/// returned key shares a quota, so this is the whole of the global limiter's behaviour.
/// </summary>
public static class RateLimitPartitions
{
    public const string Exempt = "exempt";

    private const int AuthenticatedPermitLimit = 100;
    private const int AnonymousPermitLimit = 30;

    /// <summary>
    /// Stripe authenticates its webhook by signature rather than by session, and SignalR frames are
    /// not discrete requests. Counting either against a quota rejects legitimate traffic. Matching
    /// on segments rather than a raw prefix keeps a look-alike path from inheriting the exemption.
    /// </summary>
    public static bool IsExempt(PathString path)
        => path.StartsWithSegments("/api/payments/webhook") || path.StartsWithSegments("/hubs");

    /// <summary>
    /// An identified caller is accountable and gets the larger budget; an anonymous one is keyed on
    /// address and gets less. The prefixes are load-bearing: without them a user id could collide
    /// with an address and silently share its bucket.
    /// </summary>
    public static RateLimitBucket For(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId != null)
            return new RateLimitBucket($"u:{userId}", AuthenticatedPermitLimit);

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return new RateLimitBucket($"ip:{ip}", AnonymousPermitLimit);
    }
}
