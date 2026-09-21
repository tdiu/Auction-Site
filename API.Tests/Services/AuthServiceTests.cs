using API.Core;
using API.Data;
using API.DTOs;
using API.Entities;
using API.Interfaces;
using API.Services;
using API.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace API.Tests.Services;

public class AuthServiceTests
{
    private sealed record Harness(
        AuthService Sut,
        UserManager<AppUser> UserManager,
        InMemoryRefreshSessionStore Sessions,
        ITokenService TokenService,
        List<RecordingTransaction> Transactions);

    private static async Task<Harness> CreateContext()
    {
        var tokenService = Substitute.For<ITokenService>();
        tokenService.CreateToken(Arg.Any<AppUser>()).Returns("test-token");

        // Each issue is distinct, so "which session is this row" is answerable from the hash alone
        // and a second login is visibly a second credential rather than the same one twice.
        var issued = 0;
        tokenService.GenerateRefreshToken().Returns(_ => $"refresh-{++issued}");
        tokenService.HashRefreshToken(Arg.Any<string>()).Returns(ci => $"hashed-{ci.Arg<string>()}");

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase($"AuthTests-{Guid.NewGuid()}"));

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

        var sessions = new InMemoryRefreshSessionStore();
        var transactions = new List<RecordingTransaction>();

        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.RefreshSessions.Returns(sessions);
        unitOfWork.CompleteAsync().Returns(true);
        unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var tx = new RecordingTransaction();
            transactions.Add(tx);
            return Task.FromResult<IDbContextTransaction>(tx);
        });

        var sut = new AuthService(userManager, tokenService, unitOfWork, NullLogger<AuthService>.Instance);
        return new Harness(sut, userManager, sessions, tokenService, transactions);
    }

    private static async Task<AppUser> CreateUserAsync(UserManager<AppUser> userManager, string name)
    {
        var user = new AppUser { DisplayName = name, UserName = name, Email = $"{name}@test.com" };
        var result = await userManager.CreateAsync(user, "Pass123");
        Assert.True(result.Succeeded);
        return user;
    }

    private static RefreshSession SeedSession(
        InMemoryRefreshSessionStore sessions,
        string userId,
        string token,
        DateTimeOffset? expiresAt = null)
        => sessions.Seed(new RefreshSession
        {
            UserId = userId,
            TokenHash = $"hashed-{token}",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7)
        });

    // ---- register ----

    [Fact]
    public async Task RegisterAsync_WithValidData_ReturnsAuthResultAndCreatesOneLiveSession()
    {
        var h = await CreateContext();

        var result = await h.Sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "newuser",
            Email = "new@test.com",
            Password = "Pass123"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("newuser", result.Value!.User.DisplayName);
        Assert.Equal("new@test.com", result.Value.User.Email);
        Assert.Equal("test-token", result.Value.User.Token);
        Assert.Equal("refresh-1", result.Value.RefreshToken);

        var persisted = await h.UserManager.FindByEmailAsync("new@test.com");
        Assert.NotNull(persisted);
        Assert.Equal("newuser", persisted!.DisplayName);

        var session = Assert.Single(h.Sessions.Rows);
        Assert.Equal(persisted.Id, session.UserId);
        Assert.Equal("hashed-refresh-1", session.TokenHash);   // only the HMAC is stored
        Assert.Null(session.RevokedAt);
        Assert.Null(session.ReplacedById);
        Assert.Equal(result.Value.RefreshTokenExpiry, session.ExpiresAt);

        h.TokenService.Received(1).CreateToken(Arg.Is<AppUser>(u => u.DisplayName == "newuser"));
        h.TokenService.Received(1).GenerateRefreshToken();
    }

    [Fact]
    public async Task RegisterAsync_RecordsTheUserAgentOnTheSession()
    {
        var h = await CreateContext();

        await h.Sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "newuser",
            Email = "new@test.com",
            Password = "Pass123"
        }, "Mozilla/5.0 (test)");

        Assert.Equal("Mozilla/5.0 (test)", Assert.Single(h.Sessions.Rows).UserAgent);
    }

    [Fact]
    public async Task RegisterAsync_TruncatesAnOverlongUserAgent()
    {
        var h = await CreateContext();

        await h.Sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "newuser",
            Email = "new@test.com",
            Password = "Pass123"
        }, new string('x', 600));

        // Client-controlled and unbounded on the wire, so it must not be able to widen the row.
        Assert.Equal(512, Assert.Single(h.Sessions.Rows).UserAgent!.Length);
    }

    [Fact]
    public async Task RegisterAsync_TrimsDisplayNameAndEmailBeforePersisting()
    {
        var h = await CreateContext();

        var result = await h.Sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "  newuser  ",
            Email = "  new@test.com  ",
            Password = "Pass123"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("newuser", result.Value!.User.DisplayName);
        Assert.Equal("new@test.com", result.Value.User.Email);

        var persisted = await h.UserManager.FindByEmailAsync("new@test.com");
        Assert.NotNull(persisted);
        Assert.Equal("newuser", persisted!.DisplayName);
        Assert.Equal("newuser", persisted.UserName);
        Assert.Equal(persisted.Id, Assert.Single(h.Sessions.Rows).UserId);
    }

    [Fact]
    public async Task RegisterAsync_WithWhitespaceDisplayName_ReturnsFailure()
    {
        var h = await CreateContext();

        var result = await h.Sut.RegisterAsync(new RegisterDto
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
        Assert.Empty(h.Sessions.Rows);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateDisplayName_ReturnsFailure()
    {
        var h = await CreateContext();
        await CreateUserAsync(h.UserManager, "dupe");

        var result = await h.Sut.RegisterAsync(new RegisterDto
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
        var h = await CreateContext();
        await CreateUserAsync(h.UserManager, "dupe");

        var result = await h.Sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "  dupe  ",
            Email = "second@test.com",
            Password = "Pass123"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Username already exists", result.Error);
        Assert.Equal(FailureReason.Validation, result.Reason);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ReturnsFailure()
    {
        var h = await CreateContext();
        await CreateUserAsync(h.UserManager, "first");

        var result = await h.Sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "second",
            Email = "first@test.com",
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
        var h = await CreateContext();

        var result = await h.Sut.RegisterAsync(new RegisterDto
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
        Assert.Empty(h.Sessions.Rows);
    }

    // ---- login ----

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsAuthResultAndCreatesOneLiveSession()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "loginuser");

        var result = await h.Sut.LoginAsync(new LoginDto { Email = "loginuser@test.com", Password = "Pass123" });

        Assert.True(result.IsSuccess);
        Assert.Equal("loginuser", result.Value!.User.DisplayName);
        Assert.Equal("test-token", result.Value.User.Token);
        Assert.Equal("refresh-1", result.Value.RefreshToken);

        var session = Assert.Single(h.Sessions.Rows);
        Assert.Equal(user.Id, session.UserId);
        Assert.Equal("hashed-refresh-1", session.TokenHash);
        Assert.Null(session.RevokedAt);

        h.TokenService.Received(1).CreateToken(Arg.Is<AppUser>(u => u.DisplayName == "loginuser"));
    }

    [Fact] // the point of the whole change: a phone login no longer ends the laptop's session
    public async Task LoginAsync_OnASecondDevice_LeavesTheFirstSessionLive()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "loginuser");
        var creds = new LoginDto { Email = "loginuser@test.com", Password = "Pass123" };

        var laptop = await h.Sut.LoginAsync(creds, "laptop-agent");
        var phone = await h.Sut.LoginAsync(creds, "phone-agent");

        Assert.True(laptop.IsSuccess);
        Assert.True(phone.IsSuccess);
        Assert.NotEqual(laptop.Value!.RefreshToken, phone.Value!.RefreshToken);

        Assert.Equal(2, h.Sessions.Rows.Count);
        Assert.All(h.Sessions.Rows, s => Assert.Null(s.RevokedAt));
        Assert.All(h.Sessions.Rows, s => Assert.Equal(user.Id, s.UserId));
        Assert.Equal(["laptop-agent", "phone-agent"], h.Sessions.Rows.Select(s => s.UserAgent));
    }

    [Fact]
    public async Task LoginAsync_TrimsEmailBeforeLookup()
    {
        var h = await CreateContext();
        await CreateUserAsync(h.UserManager, "loginuser");

        var result = await h.Sut.LoginAsync(new LoginDto { Email = "  loginuser@test.com  ", Password = "Pass123" });

        Assert.True(result.IsSuccess);
        Assert.Equal("loginuser@test.com", result.Value!.User.Email);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ReturnsFailure()
    {
        var h = await CreateContext();

        var result = await h.Sut.LoginAsync(new LoginDto { Email = "nobody@test.com", Password = "Pass123" });

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid Credentials", result.Error);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);
        Assert.Empty(h.Sessions.Rows);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsFailure()
    {
        var h = await CreateContext();
        await CreateUserAsync(h.UserManager, "loginuser");

        var result = await h.Sut.LoginAsync(new LoginDto { Email = "loginuser@test.com", Password = "WrongPass1" });

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid Credentials", result.Error);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);
        Assert.Empty(h.Sessions.Rows);
    }

    // ---- refresh: the rotation itself ----

    [Fact]
    public async Task RefreshTokenAsync_WithLiveSession_RotatesAndSupersedesItsPredecessor()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "refreshuser");
        var predecessor = SeedSession(h.Sessions, user.Id, "old-token");

        var result = await h.Sut.RefreshTokenAsync("old-token", "phone-agent", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("test-token", result.Value!.User.Token);
        Assert.Equal("refresh-1", result.Value.RefreshToken);

        Assert.Equal(2, h.Sessions.Rows.Count);
        var successor = h.Sessions.Rows.Single(s => s.Id != predecessor.Id);

        // Superseded, not orphaned: the predecessor is closed and points at the row that replaced it.
        Assert.NotNull(predecessor.RevokedAt);
        Assert.Equal(SessionRevokedReason.Rotated, predecessor.RevokedReason);
        Assert.Equal(successor.Id, predecessor.ReplacedById);

        Assert.Null(successor.RevokedAt);
        Assert.Equal("hashed-refresh-1", successor.TokenHash);
        Assert.Equal("phone-agent", successor.UserAgent);
        Assert.Equal(result.Value.RefreshTokenExpiry, successor.ExpiresAt);

        Assert.True(Assert.Single(h.Transactions).Committed);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithUnknownToken_ReturnsUnauthorized()
    {
        var h = await CreateContext();

        var result = await h.Sut.RefreshTokenAsync("missing-token", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid refresh token", result.Error);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);

        h.TokenService.DidNotReceive().CreateToken(Arg.Any<AppUser>());
        h.TokenService.DidNotReceive().GenerateRefreshToken();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithExpiredSession_ReturnsUnauthorizedAndLeavesTheRowAlone()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "expireduser");
        var session = SeedSession(h.Sessions, user.Id, "expired-token", DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await h.Sut.RefreshTokenAsync("expired-token", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);

        // Expiry is not evidence of anything, so nothing is revoked and no cascade fires.
        Assert.Null(session.RevokedAt);
        Assert.Single(h.Sessions.Rows);
        h.TokenService.DidNotReceive().GenerateRefreshToken();
    }

    // ---- refresh: replay ----

    [Fact]
    public async Task RefreshTokenAsync_ReplayInsideGrace_ReturnsAccessTokenOnlyAndRotatesNothing()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "graceuser");

        var successor = SeedSession(h.Sessions, user.Id, "new-token");
        var predecessor = SeedSession(h.Sessions, user.Id, "old-token");
        predecessor.RevokedAt = DateTimeOffset.UtcNow;          // just rotated, e.g. by another tab
        predecessor.RevokedReason = SessionRevokedReason.Rotated;
        predecessor.ReplacedById = successor.Id;

        var result = await h.Sut.RefreshTokenAsync("old-token", ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("test-token", result.Value!.User.Token);

        // No successor token and no cookie: the winning tab already wrote the live one, and only the
        // hash was ever stored so handing this caller a refresh token is not even possible.
        Assert.Null(result.Value.RefreshToken);
        Assert.Null(result.Value.RefreshTokenExpiry);

        Assert.Equal(2, h.Sessions.Rows.Count);                 // nothing minted
        Assert.Null(successor.RevokedAt);                       // nothing revoked
        h.TokenService.DidNotReceive().GenerateRefreshToken();
    }

    [Fact]
    public async Task RefreshTokenAsync_ReplayOutsideGrace_RevokesEverySessionForTheUser()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "reuseuser");

        var successor = SeedSession(h.Sessions, user.Id, "new-token");
        var otherDevice = SeedSession(h.Sessions, user.Id, "other-device-token");
        var predecessor = SeedSession(h.Sessions, user.Id, "old-token");
        predecessor.RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-1);   // well past RotationGrace
        predecessor.RevokedReason = SessionRevokedReason.Rotated;
        predecessor.ReplacedById = successor.Id;

        var result = await h.Sut.RefreshTokenAsync("old-token", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid refresh token", result.Error);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);

        // Two parties hold credentials for a live chain and there is no telling which is genuine.
        Assert.Equal(SessionRevokedReason.ReuseDetected, successor.RevokedReason);
        Assert.Equal(SessionRevokedReason.ReuseDetected, otherDevice.RevokedReason);
        Assert.All(h.Sessions.Rows, s => Assert.NotNull(s.RevokedAt));

        // The row that caused the cascade keeps its own reason, so victims stay tellable from causes.
        Assert.Equal(SessionRevokedReason.Rotated, predecessor.RevokedReason);
    }

    [Fact]
    public async Task RefreshTokenAsync_ReplayInsideGraceButSuccessorRevoked_CascadesInsteadOfServingGrace()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "graceuser");

        var successor = SeedSession(h.Sessions, user.Id, "new-token");
        successor.RevokedAt = DateTimeOffset.UtcNow;                    // chain already burned
        successor.RevokedReason = SessionRevokedReason.ReuseDetected;

        var predecessor = SeedSession(h.Sessions, user.Id, "old-token");
        predecessor.RevokedAt = DateTimeOffset.UtcNow;                  // inside the window on time alone
        predecessor.RevokedReason = SessionRevokedReason.Rotated;
        predecessor.ReplacedById = successor.Id;

        var result = await h.Sut.RefreshTokenAsync("old-token", ct: TestContext.Current.CancellationToken);

        // Recency is not enough — the grace window must not serve a token off a dead chain.
        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);
        h.TokenService.DidNotReceive().CreateToken(Arg.Any<AppUser>());
    }

    [Fact]
    public async Task RefreshTokenAsync_ReplayOfALoggedOutSession_RefusesWithoutCascading()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "logoutuser");

        var otherDevice = SeedSession(h.Sessions, user.Id, "other-device-token");
        var loggedOut = SeedSession(h.Sessions, user.Id, "old-token");
        loggedOut.RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        loggedOut.RevokedReason = SessionRevokedReason.LoggedOut;

        var result = await h.Sut.RefreshTokenAsync("old-token", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);

        // No live chain sits behind a logged-out token, so cascading would punish a race and
        // protect nothing. The user's other device stays signed in.
        Assert.Null(otherDevice.RevokedAt);
    }

    [Fact] // the F8 loser: another caller committed the rotation while this one was reading
    public async Task RefreshTokenAsync_WhenTheRotationClaimIsLost_RollsBackAndServesGrace()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "raceuser");
        var predecessor = SeedSession(h.Sessions, user.Id, "old-token");

        // Stand in for the winner's committed transaction, applied between this caller's read and
        // its claim: the row is rotated and already linked to a live successor.
        h.Sessions.BeforeNextClaim = row =>
        {
            var winner = SeedSession(h.Sessions, user.Id, "winner-token");
            row.RevokedAt = DateTimeOffset.UtcNow;
            row.RevokedReason = SessionRevokedReason.Rotated;
            row.ReplacedById = winner.Id;
        };

        var result = await h.Sut.RefreshTokenAsync("old-token", ct: TestContext.Current.CancellationToken);

        // Not an attack — a tab restore. It is served from the grace branch, access token only.
        Assert.True(result.IsSuccess);
        Assert.Equal("test-token", result.Value!.User.Token);
        Assert.Null(result.Value.RefreshToken);

        Assert.Equal(2, h.Sessions.Rows.Count);     // the loser minted nothing
        Assert.Equal(SessionRevokedReason.Rotated, predecessor.RevokedReason);

        var tx = Assert.Single(h.Transactions);
        Assert.True(tx.RolledBack);
        Assert.False(tx.Committed);
    }

    [Fact] // F11: a week-long chain must not outlive a lockout
    public async Task RefreshTokenAsync_WhenTheUserIsLockedOut_RevokesEverySessionAndRefuses()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "lockeduser");
        await h.UserManager.SetLockoutEnabledAsync(user, true);
        await h.UserManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));

        var presented = SeedSession(h.Sessions, user.Id, "old-token");
        var otherDevice = SeedSession(h.Sessions, user.Id, "other-device-token");

        var result = await h.Sut.RefreshTokenAsync("old-token", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);

        // If the account is locked the credentials behind it are in question, so every device goes.
        Assert.Equal(SessionRevokedReason.LoggedOut, presented.RevokedReason);
        Assert.Equal(SessionRevokedReason.LoggedOut, otherDevice.RevokedReason);
        h.TokenService.DidNotReceive().GenerateRefreshToken();
    }

    // ---- logout ----

    [Fact]
    public async Task LogoutAsync_RevokesOnlyThePresentedSession()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "logoutuser");
        var phone = SeedSession(h.Sessions, user.Id, "phone-token");
        var laptop = SeedSession(h.Sessions, user.Id, "laptop-token");

        var result = await h.Sut.LogoutAsync("phone-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(phone.RevokedAt);
        Assert.Equal(SessionRevokedReason.LoggedOut, phone.RevokedReason);

        // Signing out of a phone leaves the laptop signed in.
        Assert.Null(laptop.RevokedAt);
    }

    [Fact]
    public async Task LogoutAsync_WithUnknownToken_SucceedsAndLeavesSessionsAlone()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "logoutuser");
        var live = SeedSession(h.Sessions, user.Id, "phone-token");

        var result = await h.Sut.LogoutAsync("missing-token", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(live.RevokedAt);
    }

    [Fact]
    public async Task LogoutAsync_WithAnAlreadyRevokedSession_IsANoOpAndKeepsTheOriginalReason()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "logoutuser");
        var rotated = SeedSession(h.Sessions, user.Id, "old-token");
        var revokedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        rotated.RevokedAt = revokedAt;
        rotated.RevokedReason = SessionRevokedReason.Rotated;

        var result = await h.Sut.LogoutAsync("old-token", CancellationToken.None);

        // Logging out twice is not an attack, and must not overwrite the reason a later replay reads.
        Assert.True(result.IsSuccess);
        Assert.Equal(revokedAt, rotated.RevokedAt);
        Assert.Equal(SessionRevokedReason.Rotated, rotated.RevokedReason);
    }

    [Fact]
    public async Task LogoutAsync_WithNoCookie_Succeeds()
    {
        var h = await CreateContext();

        var result = await h.Sut.LogoutAsync(null, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ---- lockout ----

    [Fact]
    public async Task LoginAsync_WithWrongPassword_IncrementsTheFailureCount()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "counted");

        await h.Sut.LoginAsync(new LoginDto { Email = user.Email!, Password = "Wrong1" });

        Assert.Equal(1, await h.UserManager.GetAccessFailedCountAsync(user));
        Assert.False(await h.UserManager.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task LoginAsync_AfterFiveFailures_LocksTheAccount()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "lockme");

        for (var attempt = 0; attempt < 5; attempt++)
            await h.Sut.LoginAsync(new LoginDto { Email = user.Email!, Password = "Wrong1" });

        Assert.True(await h.UserManager.IsLockedOutAsync(user));
        Assert.Empty(h.Sessions.Rows);
    }

    [Fact]
    public async Task LoginAsync_WhenLockedOut_RefusesEvenWithTheCorrectPassword()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "locked");
        await h.UserManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15));

        var result = await h.Sut.LoginAsync(new LoginDto { Email = user.Email!, Password = "Pass123" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Locked, result.Reason);
        Assert.Empty(h.Sessions.Rows);
    }

    [Fact]
    public async Task LoginAsync_WhenLockedOut_DoesNotExtendTheWindow()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "noextend");
        await h.UserManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15));
        var lockedUntil = await h.UserManager.GetLockoutEndDateAsync(user);

        await h.Sut.LoginAsync(new LoginDto { Email = user.Email!, Password = "Wrong1" });

        Assert.Equal(lockedUntil, await h.UserManager.GetLockoutEndDateAsync(user));
        Assert.Equal(0, await h.UserManager.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task LoginAsync_OnSuccess_ClearsTheFailureCount()
    {
        var h = await CreateContext();
        var user = await CreateUserAsync(h.UserManager, "cleared");
        await h.Sut.LoginAsync(new LoginDto { Email = user.Email!, Password = "Wrong1" });
        Assert.Equal(1, await h.UserManager.GetAccessFailedCountAsync(user));

        var result = await h.Sut.LoginAsync(new LoginDto { Email = user.Email!, Password = "Pass123" });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, await h.UserManager.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_CannotLockAnAccountThatDoesNotExist()
    {
        var h = await CreateContext();

        for (var attempt = 0; attempt < 6; attempt++)
            await h.Sut.LoginAsync(new LoginDto { Email = "nobody@test.com", Password = "Wrong1" });

        Assert.Equal(0, await h.UserManager.Users.CountAsync(TestContext.Current.CancellationToken));
    }

    // ---- external login ----

    [Fact]
    public async Task ExternalLoginAsync_NewUser_CreatesAccountWithGeneratedNameAndNoDob()
    {
        var h = await CreateContext();

        var result = await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-1", "alex.smith@example.com", "Alex Smith"),
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);

        var user = await h.UserManager.FindByLoginAsync("Google", "sub-1");
        Assert.NotNull(user);
        Assert.Equal("alexsmith", user!.DisplayName);
        Assert.Equal(user.DisplayName, user.UserName);
        Assert.Null(user.DateOfBirth);
        Assert.Null(user.PasswordHash);
        Assert.True(user.EmailConfirmed);
        Assert.Equal("Google", result.Value!.User.AuthProvider);

        var session = Assert.Single(h.Sessions.Rows);
        Assert.Equal(user.Id, session.UserId);
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public async Task ExternalLoginAsync_NameAlreadyTaken_SuffixesAndKeepsBothUsers()
    {
        var h = await CreateContext();
        await h.UserManager.CreateAsync(new AppUser
        {
            DisplayName = "alexsmith",
            UserName = "alexsmith",
            Email = "other@test.com"
        });

        var result = await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-2", "alex.smith@example.com", "Alex Smith"),
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.StartsWith("alexsmith", result.Value!.User.DisplayName);
        Assert.NotEqual("alexsmith", result.Value.User.DisplayName);
        Assert.Equal(2, await h.UserManager.Users.CountAsync(TestContext.Current.CancellationToken));

        var tx = Assert.Single(h.Transactions);
        Assert.Contains("attempt", tx.SavepointRollbacks);
        Assert.True(tx.Committed);
    }

    [Fact]
    public async Task ExternalLoginAsync_ReturningUser_MatchesOnSubAndDoesNotDuplicate()
    {
        var h = await CreateContext();
        await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-3", "alex@example.com", "Alex Smith"),
            ct: TestContext.Current.CancellationToken);

        var result = await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-3", "moved@example.com", "Alex Smith"),
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, await h.UserManager.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal("alex@example.com", result.Value!.User.Email);
        Assert.Equal(2, h.Sessions.Rows.Count);
    }

    [Fact]
    public async Task ExternalLoginAsync_EmailBelongsToPasswordAccount_RefusesAndCreatesNothing()
    {
        var h = await CreateContext();
        await h.Sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "alex",
            Email = "alex@example.com",
            Password = "Pass123"
        });

        var result = await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-4", "alex@example.com", "Alex Smith"),
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Conflict, result.Reason);
        Assert.Equal(1, await h.UserManager.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Null(await h.UserManager.FindByLoginAsync("Google", "sub-4"));
    }

    [Fact]
    public async Task ExternalLoginAsync_NoEmailClaim_RefusesWithoutCreatingAnAccount()
    {
        var h = await CreateContext();

        var result = await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-5", null, "Alex Smith"),
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Validation, result.Reason);
        Assert.Equal(0, await h.UserManager.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(h.Sessions.Rows);
    }

    [Fact]
    public async Task ExternalLoginAsync_EmptyNameClaim_FallsBackToEmailLocalPart()
    {
        var h = await CreateContext();

        var result = await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-6", "alex.smith@example.com", ""),
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("alexsmith", result.Value!.User.DisplayName);
    }

    [Fact]
    public async Task ExternalLoginAsync_WhenTheUserIsLockedOut_RefusesWithoutIssuingASession()
    {
        var h = await CreateContext();
        await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-7", "alex@example.com", "Alex Smith"),
            ct: TestContext.Current.CancellationToken);

        var user = await h.UserManager.FindByLoginAsync("Google", "sub-7");
        await h.UserManager.SetLockoutEnabledAsync(user!, true);
        await h.UserManager.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(5));
        var sessionsBefore = h.Sessions.Rows.Count;

        var result = await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-7", "alex@example.com", "Alex Smith"),
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Locked, result.Reason);
        Assert.Equal(sessionsBefore, h.Sessions.Rows.Count);
    }

    [Fact]
    public async Task LoginAsync_AgainstPasswordlessExternalAccount_IsRejected()
    {
        var h = await CreateContext();
        await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-8", "alex@example.com", "Alex Smith"),
            ct: TestContext.Current.CancellationToken);
        var sessionsBefore = h.Sessions.Rows.Count;

        var result = await h.Sut.LoginAsync(new LoginDto { Email = "alex@example.com", Password = "Pass123" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureReason.Unauthorized, result.Reason);
        Assert.Equal(sessionsBefore, h.Sessions.Rows.Count);
    }

    [Fact]
    public async Task RegisterAsync_WithEmailHeldByExternalAccount_IsRejected()
    {
        var h = await CreateContext();
        await h.Sut.ExternalLoginAsync(
            new ExternalLoginRequest("Google", "sub-9", "alex@example.com", "Alex Smith"),
            ct: TestContext.Current.CancellationToken);

        var result = await h.Sut.RegisterAsync(new RegisterDto
        {
            DisplayName = "alex2",
            Email = "alex@example.com",
            Password = "Pass123"
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("Email is already taken", result.ValidationErrors!["email"]);
        Assert.Equal(1, await h.UserManager.Users.CountAsync(TestContext.Current.CancellationToken));
    }
}
