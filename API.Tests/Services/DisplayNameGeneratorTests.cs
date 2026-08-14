using API.Services;
using Xunit;

namespace API.Tests.Services;

public class DisplayNameGeneratorTests
{
    private const int RegisterDtoMaxLength = 15;

    [Theory]
    [InlineData("Alex Smith", "alex@x.com", "alexsmith")]
    [InlineData("Renée Fleming", "r@x.com", "reneeflemin")]
    [InlineData("!!! ???", "alex.smith@x.com", "alexsmith")]
    [InlineData("", "!!@x.com", "user")]
    [InlineData(null, "!!@x.com", "user")]
    [InlineData("Bartholomew Cubbins", "b@x.com", "bartholomew")]
    [InlineData("Дмитрий", "dmitri@x.com", "dmitri")]
    [InlineData("田中太郎", "!!@x.com", "user")]
    public void Derive_ProducesExpectedBaseName(string? name, string email, string expected)
        => Assert.Equal(expected, DisplayNameGenerator.Derive(name, email));

    /// <summary>
    /// Identity's default <c>AllowedUserNameCharacters</c> is ASCII, and the generated name is
    /// assigned straight to <c>UserName</c>. A slug that kept any Unicode letter would come back
    /// from <c>CreateAsync</c> as <c>InvalidUserName</c>, which the retry loop treats as terminal,
    /// so the account could never be created and the user could never correct it.
    /// </summary>
    [Theory]
    [InlineData("Дмитрий")]
    [InlineData("田中太郎")]
    [InlineData("Renée Fleming")]
    [InlineData("J.R. O'Brien")]
    [InlineData("Mary-Jane Watson")]
    public void Derive_IsAlwaysAsciiAlphanumeric(string name)
    {
        var derived = DisplayNameGenerator.Derive(name, "someone@example.com");

        Assert.Matches("^[a-z0-9]+$", derived);
        Assert.InRange(derived.Length, 3, 11);
    }

    [Theory]
    [InlineData("Дмитрий")]
    [InlineData("Mary-Jane Watson")]
    [InlineData("Bartholomew Cubbins")]
    [InlineData("")]
    public void EveryCandidate_IsAsciiAlphanumericAndWithinTheRegisterLimit(string name)
    {
        var baseName = DisplayNameGenerator.Derive(name, "someone@example.com");

        for (var attempt = 0; attempt < DisplayNameGenerator.MaxAttempts; attempt++)
        {
            var candidate = DisplayNameGenerator.Candidate(baseName, attempt);

            Assert.Matches("^[a-z0-9]+$", candidate);
            Assert.InRange(candidate.Length, 3, RegisterDtoMaxLength);
        }
    }

    [Fact]
    public void Candidate_AttemptZero_IsTheCleanName()
        => Assert.Equal("alexsmith", DisplayNameGenerator.Candidate("alexsmith", 0));

    [Fact]
    public void Candidate_LaterAttempts_DifferFromTheCleanName()
    {
        var suffixed = Enumerable.Range(1, DisplayNameGenerator.MaxAttempts - 1)
            .Select(attempt => DisplayNameGenerator.Candidate("alexsmith", attempt))
            .ToList();

        Assert.All(suffixed, c => Assert.NotEqual("alexsmith", c));
    }

    /// <summary>
    /// The last arm of <c>Candidate</c> is the escape hatch that makes a signup impossible to fail
    /// for want of a name: it must not be derived from the colliding base at all.
    /// </summary>
    [Fact]
    public void Candidate_FinalAttempt_AbandonsTheBaseName()
    {
        var final = DisplayNameGenerator.Candidate("alexsmith", DisplayNameGenerator.MaxAttempts - 1);

        Assert.DoesNotContain("alexsmith", final);
        Assert.StartsWith("user", final);
    }
}
