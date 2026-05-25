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
    private async Task<(AuthService sut, UserManager<AppUser> userManager, AppDbContext db, iTokenService tokenService)> CreateContext()
    {
        var tokenService = Substitute.For<iTokenService>();
        tokenService.CreateToken(Arg.Any<AppUser>()).Returns("test-token");

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
    public async Task RegisterAsync_WithValidData_ReturnsSuccessAndUserDto()
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
        Assert.Equal("newuser", result.Value!.DisplayName);
        Assert.Equal("new@test.com", result.Value!.Email);
        Assert.Equal("test-token", result.Value!.Token);

        var persisted = await userManager.FindByEmailAsync("new@test.com");
        Assert.NotNull(persisted);
        Assert.Equal("newuser", persisted!.DisplayName);

        tokenService.Received(1).CreateToken(Arg.Is<AppUser>(u => u.DisplayName == "newuser"));
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
        Assert.Contains("already taken", result.Error);
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
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsSuccessAndUserDto()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var user = new AppUser { DisplayName = "loginuser", UserName = "loginuser", Email = "login@test.com" };
        await userManager.CreateAsync(user, "Pass123");

        var result = await sut.LoginAsync(new LoginDto { Email = "login@test.com", Password = "Pass123" });

        Assert.True(result.IsSuccess);
        Assert.Equal("loginuser", result.Value!.DisplayName);
        Assert.Equal("login@test.com", result.Value!.Email);
        Assert.Equal("test-token", result.Value!.Token);

        tokenService.Received(1).CreateToken(Arg.Is<AppUser>(u => u.DisplayName == "loginuser"));
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ReturnsFailure()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var result = await sut.LoginAsync(new LoginDto { Email = "nobody@test.com", Password = "Pass123" });

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid Email", result.Error);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsFailure()
    {
        var (sut, userManager, db, tokenService) = await CreateContext();

        var user = new AppUser { DisplayName = "loginuser", UserName = "loginuser", Email = "login@test.com" };
        await userManager.CreateAsync(user, "Pass123");

        var result = await sut.LoginAsync(new LoginDto { Email = "login@test.com", Password = "WrongPass1" });

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid Password", result.Error);
    }
}
