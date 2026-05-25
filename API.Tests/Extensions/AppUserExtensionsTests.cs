using API.Entities;
using API.Extensions;
using API.Interfaces;
using NSubstitute;
using Xunit;

namespace API.Tests.Extensions;

public class AppUserExtensionsTests
{
    [Fact]
    public void ToDto_MapsAllAppUserFieldsToUserDto()
    {
        var tokenService = Substitute.For<iTokenService>();
        tokenService.CreateToken(Arg.Any<AppUser>()).Returns("test-token");

        var user = new AppUser
        {
            Id = "user-id-123",
            DisplayName = "TestUser",
            Email = "test@test.com",
            ImageUrl = "http://example.com/image.jpg"
        };

        var result = user.ToDto(tokenService);

        Assert.Equal("user-id-123", result.Id);
        Assert.Equal("TestUser", result.DisplayName);
        Assert.Equal("test@test.com", result.Email);
        Assert.Equal("http://example.com/image.jpg", result.ImageUrl);
        Assert.Equal("test-token", result.Token);
    }

    [Fact]
    public void ToDto_CallsTokenServiceWithCorrectUser()
    {
        var tokenService = Substitute.For<iTokenService>();
        tokenService.CreateToken(Arg.Any<AppUser>()).Returns("test-token");

        var user = new AppUser { Id = "user-id", DisplayName = "TestUser", Email = "test@test.com" };

        user.ToDto(tokenService);

        tokenService.Received(1).CreateToken(user);
    }

    [Fact]
    public void ToDto_WithNullImageUrl_ReturnsNullImageUrl()
    {
        var tokenService = Substitute.For<iTokenService>();
        tokenService.CreateToken(Arg.Any<AppUser>()).Returns("test-token");

        var user = new AppUser { Id = "user-id", DisplayName = "TestUser", Email = "test@test.com", ImageUrl = null };

        var result = user.ToDto(tokenService);

        Assert.Null(result.ImageUrl);
    }
}
