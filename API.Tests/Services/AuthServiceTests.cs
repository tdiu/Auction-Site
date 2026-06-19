using API.Core;
using API.Data;
using API.DTOs;
using API.Entities;
using API.Interfaces;
using API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace API.Tests.Services;

public class AuthServiceTests
{
    private async Task<(AuthService sut, UserManager<AppUser> userManager, AppDbContext db, ITokenService tokenService)> CreateContext()
    {
        var tokenService = Substitute.For<ITokenService>();
        tokenService.CreateToken(Arg.Any<AppUser>()).Returns("test-token");
        tokenService.GenerateRefreshToken().Returns("test-refresh-token");

        var services = new ServiceCollection();
        var dbName = $"AuthTests-{Guid.NewGuid()}";
        services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(dbName));

        services.AddIdentityCore<AppUser>(options =>
        {
            options.Password.RequireNonAlphanumeric = false;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AppDbContext>();

        var sp = services.BuildServiceProvider();
        var userManager = sp.GetRequiredService<UserManager<AppUser>>();
        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var sut = new AuthService(userManager, tokenService);
        return (sut, userManager, db, tokenService);
    }

    [Fact]
    public async Task RegisterAsync_WithValidData_ReturnsSuccessAndAuthResult()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var dto = new RegisterDto
        {
            DisplayName = "newuser",
            Email = "new@test.com",
            Password = "Pass123"
        };

        var result = await sut.RegisterAsync(dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("newuser", result.Value!.User.DisplayName);
        Assert.Equal("new@test.com", result.Value.User.Email);
        Assert.Equal("test-token", result.Value.User.Token);
        Assert.Equal("test-refresh-token", result.Value.RefreshToken);

        var persisted = await userManager.FindByEmailAsync("new@test.com");
        Assert.NotNull(persisted);
        Assert.Equal("newuser", persisted!.DisplayName);
        Assert.Equal("test-refresh-token", persisted.RefreshToken);
        Assert.Equal(result.Value.RefreshTokenExpiry, persisted.RefreshTokenExpiry);

        tokenService.Received(1).CreateToken(Arg.Is<AppUser>(u => u.DisplayName == "newuser"));
        tokenService.Received(1).GenerateRefreshToken();
    }

    [Fact]
    public async Task RegisterAsync_TrimsDisplayNameAndEmailBeforePersisting()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var result = await sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "  newuser  ",
            Email = "  new@test.com  ",
            Password = "Pass123"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("newuser", result.Value!.User.DisplayName);
        Assert.Equal("new@test.com", result.Value.User.Email);
        Assert.Equal("test-refresh-token", result.Value.RefreshToken);

        var persisted = await userManager.FindByEmailAsync("new@test.com");
        Assert.NotNull(persisted);
        Assert.Equal("newuser", persisted!.DisplayName);
        Assert.Equal("newuser", persisted.UserName);
        Assert.Equal("new@test.com", persisted.Email);
        Assert.Equal("test-refresh-token", persisted.RefreshToken);
        Assert.Equal(result.Value.RefreshTokenExpiry, persisted.RefreshTokenExpiry);
    }

    [Fact]
    public async Task RegisterAsync_WithWhitespaceDisplayName_ReturnsFailure()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var result = await sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "   ",
            Email = "new@test.com",
            Password = "Pass123"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Username is required", result.Error);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("Username is required", result.ValidationErrors["displayName"]);
        Assert.Equal(FailureReason.Validation, result.Reason);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateDisplayName_ReturnsFailure()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var existing = new AppUser { DisplayName = "dupe", UserName = "dupe", Email = "first@test.com" };
        await userManager.CreateAsync(existing, "Pass123");

        var result = await sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "dupe",
            Email = "second@test.com",
            Password = "Pass123"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Username already exists", result.Error);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("Username already exists", result.ValidationErrors["displayName"]);
        Assert.Equal(FailureReason.Validation, result.Reason);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateDisplayNameAfterTrim_ReturnsFailure()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var existing = new AppUser { DisplayName = "dupe", UserName = "dupe", Email = "first@test.com" };
        await userManager.CreateAsync(existing, "Pass123");

        var result = await sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "  dupe  ",
            Email = "second@test.com",
            Password = "Pass123"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Username already exists", result.Error);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("Username already exists", result.ValidationErrors["displayName"]);
        Assert.Equal(FailureReason.Validation, result.Reason);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ReturnsFailure()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var existing = new AppUser { DisplayName = "first", UserName = "first", Email = "dupe@test.com" };
        await userManager.CreateAsync(existing, "Pass123");

        var result = await sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "second",
            Email = "dupe@test.com",
            Password = "Pass123"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Email is already taken", result.Error);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("Email is already taken", result.ValidationErrors["email"]);
        Assert.Equal(FailureReason.Validation, result.Reason);
    }

    [Fact]
    public async Task RegisterAsync_WithWeakPassword_ReturnsIdentityError()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var result = await sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "user",
            Email = "user@test.com",
            Password = "ab"
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("Passwords", result.Error);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains(result.ValidationErrors["password"], error => error.Contains("Passwords"));
        Assert.Equal(FailureReason.Validation, result.Reason);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsSuccessAndAuthResult()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var user = new AppUser { DisplayName = "loginuser", UserName = "loginuser", Email = "login@test.com" };
        await userManager.CreateAsync(user, "Pass123");

        var result = await sut.LoginAsync(new LoginDto { Email = "login@test.com", Password = "Pass123" });

        Assert.True(result.IsSuccess);
        Assert.Equal("loginuser", result.Value!.User.DisplayName);
        Assert.Equal("login@test.com", result.Value.User.Email);
        Assert.Equal("test-token", result.Value.User.Token);
        Assert.Equal("test-refresh-token", result.Value.RefreshToken);

        var persisted = await userManager.FindByEmailAsync("login@test.com");
        Assert.NotNull(persisted);
        Assert.Equal("test-refresh-token", persisted!.RefreshToken);
        Assert.Equal(result.Value.RefreshTokenExpiry, persisted.RefreshTokenExpiry);

        tokenService.Received(1).CreateToken(Arg.Is<AppUser>(u => u.DisplayName == "loginuser"));
        tokenService.Received(1).GenerateRefreshToken();
    }

    [Fact]
    public async Task LoginAsync_TrimsEmailBeforeLookup()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var user = new AppUser { DisplayName = "loginuser", UserName = "loginuser", Email = "login@test.com" };
        await userManager.CreateAsync(user, "Pass123");

        var result = await sut.LoginAsync(new LoginDto { Email = "  login@test.com  ", Password = "Pass123" });

        Assert.True(result.IsSuccess);
        Assert.Equal("login@test.com", result.Value!.User.Email);
        Assert.Equal("test-refresh-token", result.Value.RefreshToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidRefreshToken_ReturnsSuccessAndRotatesRefreshToken()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var user = new AppUser
        {
            DisplayName = "refreshuser",
            UserName = "refreshuser",
            Email = "refresh@test.com",
            RefreshToken = "old-refresh-token",
            RefreshTokenExpiry = DateTime.UtcNow.AddDays(1)
        };
        await userManager.CreateAsync(user, "Pass123");

        var result = await sut.RefreshTokenAsync("old-refresh-token");

        Assert.True(result.IsSuccess);
        Assert.Equal("refreshuser", result.Value!.User.DisplayName);
        Assert.Equal("refresh@test.com", result.Value.User.Email);
        Assert.Equal("test-token", result.Value.User.Token);
        Assert.Equal("test-refresh-token", result.Value.RefreshToken);

        var persisted = await userManager.FindByEmailAsync("refresh@test.com");
        Assert.NotNull(persisted);
        Assert.Equal("test-refresh-token", persisted!.RefreshToken);
        Assert.Equal(result.Value.RefreshTokenExpiry, persisted.RefreshTokenExpiry);

        tokenService.Received(1).CreateToken(Arg.Is<AppUser>(u => u.DisplayName == "refreshuser"));
        tokenService.Received(1).GenerateRefreshToken();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithUnknownRefreshToken_ReturnsUnauthorized()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var result = await sut.RefreshTokenAsync("missing-refresh-token");

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid refresh token", result.Error);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);

        tokenService.DidNotReceive().CreateToken(Arg.Any<AppUser>());
        tokenService.DidNotReceive().GenerateRefreshToken();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithExpiredRefreshToken_ReturnsUnauthorized()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var user = new AppUser
        {
            DisplayName = "expireduser",
            UserName = "expireduser",
            Email = "expired@test.com",
            RefreshToken = "expired-refresh-token",
            RefreshTokenExpiry = DateTime.UtcNow.AddMinutes(-1)
        };
        await userManager.CreateAsync(user, "Pass123");

        var result = await sut.RefreshTokenAsync("expired-refresh-token");

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid refresh token", result.Error);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);

        var persisted = await userManager.FindByEmailAsync("expired@test.com");
        Assert.NotNull(persisted);
        Assert.Equal("expired-refresh-token", persisted!.RefreshToken);

        tokenService.DidNotReceive().CreateToken(Arg.Any<AppUser>());
        tokenService.DidNotReceive().GenerateRefreshToken();
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ReturnsFailure()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var result = await sut.LoginAsync(new LoginDto { Email = "nobody@test.com", Password = "Pass123" });

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid Credentials", result.Error);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsFailure()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var user = new AppUser { DisplayName = "loginuser", UserName = "loginuser", Email = "login@test.com" };
        await userManager.CreateAsync(user, "Pass123");

        var result = await sut.LoginAsync(new LoginDto { Email = "login@test.com", Password = "WrongPass1" });

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid Credentials", result.Error);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);
    }
}
