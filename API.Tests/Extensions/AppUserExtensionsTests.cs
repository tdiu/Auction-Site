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

        var result = user.ToDto("test-token");

        Assert.Equal("user-id-123", result.Id);
        Assert.Equal("TestUser", result.DisplayName);
        Assert.Equal("test@test.com", result.Email);
        Assert.Equal("http://example.com/image.jpg", result.ImageUrl);
        Assert.Equal("test-token", result.Token);
    }

    [Fact]
    public void ToDto_UsesProvidedToken()
    {
        var user = new AppUser { Id = "user-id", DisplayName = "TestUser", Email = "test@test.com" };

        var result = user.ToDto("test-token");

        Assert.Equal("test-token", result.Token);
    }

    [Fact]
    public void ToDto_WithNullImageUrl_ReturnsNullImageUrl()
    {
        var user = new AppUser { Id = "user-id", DisplayName = "TestUser", Email = "test@test.com", ImageUrl = null };

        var result = user.ToDto("test-token");

        Assert.Null(result.ImageUrl);
    }
}
