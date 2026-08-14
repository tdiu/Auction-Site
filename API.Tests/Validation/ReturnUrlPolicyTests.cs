using API.Validation;
using Xunit;

namespace API.Tests.Validation;

public class ReturnUrlPolicyTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/auctions")]
    [InlineData("/auctions/42")]
    [InlineData("/messages?tab=inbox")]
    [InlineData("/evil.com")]
    public void KeepsPathsOnTheClientOrigin(string raw)
        => Assert.Equal(raw, ReturnUrlPolicy.Safe(raw));

    /// <summary>
    /// The protocol-relative forms are the whole point: appended to ClientAppUrl, <c>//evil.com</c>
    /// resolves to a different host entirely, so the redirect leaves our domain while looking like
    /// a local path.
    /// </summary>
    [Theory]
    [InlineData("//evil.com")]
    [InlineData("//evil.com/phish")]
    [InlineData("/\\evil.com")]
    [InlineData("/\\/evil.com")]
    public void RejectsProtocolRelativeUrls(string raw)
        => Assert.Equal("/", ReturnUrlPolicy.Safe(raw));

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com")]
    [InlineData("\\\\evil.com")]
    [InlineData("evil.com")]
    [InlineData(" /auctions")]
    [InlineData("javascript:alert(1)")]
    public void RejectsAnythingNotRootedAtASlash(string raw)
        => Assert.Equal("/", ReturnUrlPolicy.Safe(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FallsBackToRootWhenAbsent(string? raw)
        => Assert.Equal("/", ReturnUrlPolicy.Safe(raw));
}
