using System.Net;
using System.Security.Claims;
using API.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace API.Tests.Services;

public class RateLimitPartitionsTests
{
    [Theory]
    [InlineData("/api/payments/webhook")]
    [InlineData("/hubs")]
    [InlineData("/hubs/presence")]
    public void ExemptsSignatureAuthenticatedAndPersistentTransports(string path)
        => Assert.True(RateLimitPartitions.IsExempt(path));

    /// <summary>
    /// Segment matching rather than a prefix match, so a look-alike path cannot inherit the
    /// exemption and opt itself out of every quota.
    /// </summary>
    [Theory]
    [InlineData("/api/account/login")]
    [InlineData("/api/account/register")]
    [InlineData("/api/auctions")]
    [InlineData("/api/payments")]
    [InlineData("/hubsomething")]
    [InlineData("/api/payments/webhooks-are-not-this")]
    [InlineData("/")]
    public void DoesNotExemptAnythingElse(string path)
        => Assert.False(RateLimitPartitions.IsExempt(path));

    [Fact]
    public void AuthenticatedCallerIsKeyedOnUserIdAndGetsTheLargerBudget()
    {
        var bucket = RateLimitPartitions.For(ContextFor(userId: "user-123", ip: "203.0.113.7"));

        Assert.Equal("u:user-123", bucket.Key);
        Assert.Equal(100, bucket.PermitLimit);
    }

    [Fact]
    public void AnonymousCallerIsKeyedOnAddressAndGetsTheSmallerBudget()
    {
        var bucket = RateLimitPartitions.For(ContextFor(userId: null, ip: "203.0.113.7"));

        Assert.Equal("ip:203.0.113.7", bucket.Key);
        Assert.Equal(30, bucket.PermitLimit);
    }

    /// <summary>
    /// The identity claim wins over the connection, so two users behind one NAT get their own
    /// budgets rather than sharing the address's.
    /// </summary>
    [Fact]
    public void TwoUsersOnOneAddressGetSeparateBuckets()
    {
        var first = RateLimitPartitions.For(ContextFor("user-a", "198.51.100.4"));
        var second = RateLimitPartitions.For(ContextFor("user-b", "198.51.100.4"));

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(first.PermitLimit, second.PermitLimit);
    }

    [Fact]
    public void UnresolvableAddressFallsBackToASingleBucket()
    {
        var bucket = RateLimitPartitions.For(ContextFor(userId: null, ip: null));

        Assert.Equal("ip:unknown", bucket.Key);
        Assert.Equal(30, bucket.PermitLimit);
    }

    /// <summary>
    /// The prefixes exist for this: a user whose id is literally an address must not land in the
    /// bucket belonging to that address, nor inherit its budget.
    /// </summary>
    [Fact]
    public void UserIdCannotCollideWithAnAddress()
    {
        var user = RateLimitPartitions.For(ContextFor(userId: "203.0.113.7", ip: "192.0.2.1"));
        var anonymous = RateLimitPartitions.For(ContextFor(userId: null, ip: "203.0.113.7"));

        Assert.NotEqual(user.Key, anonymous.Key);
        Assert.NotEqual(user.PermitLimit, anonymous.PermitLimit);
    }

    private static DefaultHttpContext ContextFor(string? userId, string? ip)
    {
        var context = new DefaultHttpContext();

        if (userId != null)
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));

        context.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip);
        return context;
    }
}
