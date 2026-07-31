using API.Entities;
using API.Extensions;
using Xunit;

namespace API.Tests.Extensions;

public class AppUserExtensionsTests
{
    [Fact]
    public void ToDto_MapsAllAppUserFieldsToUserDto()
    {
        var user = new AppUser
        {
            Id = "user-id-123",
            DisplayName = "TestUser",
            Email = "test@test.com",
            ImageUrl = "http://example.com/image.jpg"
        };

        var result = user.ToDto("test-token", null);

        Assert.Equal("user-id-123", result.Id);
        Assert.Equal("TestUser", result.DisplayName);
        Assert.Equal("test@test.com", result.Email);
        Assert.Equal("http://example.com/image.jpg", result.ImageUrl);
        Assert.Equal("test-token", result.Token);
        Assert.Equal("password", result.AuthProvider);
    }

    [Fact]
    public void ToDto_UsesProvidedToken()
    {
        var user = new AppUser { Id = "user-id", DisplayName = "TestUser", Email = "test@test.com" };

        var result = user.ToDto("test-token", null);

        Assert.Equal("test-token", result.Token);
    }

    [Fact]
    public void ToDto_WithNullImageUrl_ReturnsNullImageUrl()
    {
        var user = new AppUser { Id = "user-id", DisplayName = "TestUser", Email = "test@test.com", ImageUrl = null };

        var result = user.ToDto("test-token", null);

        Assert.Null(result.ImageUrl);
    }

    // The provider name is whatever scheme AspNetUserLogins recorded, so a second provider added
    // later reports itself without this mapping changing
    [Theory]
    [InlineData("Google")]
    [InlineData("GitHub")]
    public void ToDto_WithExternalProvider_ReportsThatProvider(string provider)
    {
        var user = new AppUser { Id = "user-id", DisplayName = "TestUser", Email = "test@test.com" };

        var result = user.ToDto("test-token", provider);

        Assert.Equal(provider, result.AuthProvider);
    }

    [Fact]
    public void ToDto_WithNoExternalProvider_ReportsPassword()
    {
        var user = new AppUser { Id = "user-id", DisplayName = "TestUser", Email = "test@test.com" };

        var result = user.ToDto("test-token", null);

        Assert.Equal("password", result.AuthProvider);
    }
}
