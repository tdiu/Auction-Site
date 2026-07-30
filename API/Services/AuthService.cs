using API.Core;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

public class AuthService(UserManager<AppUser> userManager, ITokenService tokenService, IUnitOfWork unitOfWork, ILogger<AuthService> logger) : IAuthService
{
    private const int RefreshTokenLifetimeDays = 7;
    private static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(15);
    public async Task<Result<AuthResult>> RegisterAsync(RegisterDto registerDto, string? userAgent = null)
    {
        var displayName = registerDto.DisplayName.Trim();
        var email = registerDto.Email.Trim();

        if (string.IsNullOrEmpty(displayName))
            return Result<AuthResult>.ValidationFailure("displayName", "Username is required");

        if (await userManager.Users.AnyAsync(x => x.DisplayName == displayName))
            return Result<AuthResult>.ValidationFailure("displayName", "Username already exists");

        if (await userManager.Users.AnyAsync(x => x.Email == email))
            return Result<AuthResult>.ValidationFailure("email", "Email is already taken");

        var user = new AppUser
        {
            DisplayName = displayName,
            UserName = displayName,
            Email = email,
            DateOfBirth = registerDto.DateOfBirth
        };

        var res = await userManager.CreateAsync(user, registerDto.Password);
        if (!res.Succeeded)
            return Result<AuthResult>.ValidationFailure(MapIdentityErrors(res.Errors));

        return await IssueAuthTokenAsync(user, userAgent);
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginDto loginDto, string? userAgent = null)
    {
        var email = loginDto.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
            return Result<AuthResult>.Failure("Invalid Credentials", FailureReason.Unauthorized);

        var valid = await userManager.CheckPasswordAsync(user, loginDto.Password);
        if (!valid)
            return Result<AuthResult>.Failure("Invalid Credentials", FailureReason.Unauthorized);

        return await IssueAuthTokenAsync(user, userAgent);
    }

    public async Task<Result<bool>> LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return Result<bool>.Success(true);

        var session = await unitOfWork.RefreshSessions.GetByTokenHashAsync(tokenService.HashRefreshToken(refreshToken), ct);

        // Unknown or already revoked: nothing to do
        if (session is null || session.RevokedAt is not null)
            return Result<bool>.Success(true);

        session.RevokedAt = DateTimeOffset.UtcNow;
        session.RevokedReason = SessionRevokedReason.LoggedOut;
        await unitOfWork.CompleteAsync();

        return Result<bool>.Success(true);
    }

    // Creates a new session every register, login, external login
    private async Task<Result<AuthResult>> IssueAuthTokenAsync(AppUser user, string? userAgent = null)
    {
        var (session, refreshToken) = NewSession(user.Id, userAgent);
        unitOfWork.RefreshSessions.Add(session);
        await unitOfWork.CompleteAsync();

        return Result<AuthResult>.Success(new AuthResult(user.ToDto(tokenService.CreateToken(user)), refreshToken,
            session.ExpiresAt));
    }

    private (RefreshSession Session, string Token) NewSession(string userId, string? userAgent)
    {
        var token = tokenService.GenerateRefreshToken();
        var now = DateTimeOffset.UtcNow;

        return (
            new RefreshSession
            {
                UserId = userId,
                TokenHash = tokenService.HashRefreshToken(token),
                CreatedAt = now,
                ExpiresAt = now.AddDays(RefreshTokenLifetimeDays),
                UserAgent = Truncate(userAgent, 512)
            }, token);
    }

    // Rotate token
    public async Task<Result<AuthResult>> RefreshTokenAsync(string refreshToken, string? userAgent = null, CancellationToken ct = default)
    {
        var session = await unitOfWork.RefreshSessions.GetByTokenHashAsync(tokenService.HashRefreshToken(refreshToken), ct);

        if (session == null)
            return Result<AuthResult>.Failure("Invalid refresh token", FailureReason.Unauthorized);

        var now = DateTimeOffset.UtcNow;

        // Handle revoked session
        if (session.RevokedAt is not null)
            return await HandleReplayAsync(session.Id, now, ct);

        if (session.ExpiresAt <= now)
            return Result<AuthResult>.Failure("Invalid refresh token", FailureReason.Unauthorized);

        var user = await userManager.FindByIdAsync(session.UserId);
        if (user == null)
            return Result<AuthResult>.Failure("Invalid refresh token", FailureReason.Unauthorized);

        // A long refresh chain must not let someone refresh their way through a lockout
        // End all sessions instead of refusing this one
        // If an account is locked, the credentials behind it are in question too
        if (await userManager.IsLockedOutAsync(user))
        {
            await unitOfWork.RefreshSessions.RevokeAllForUserAsync(
                user.Id, SessionRevokedReason.LoggedOut, ct);
            return Result<AuthResult>.Failure("Invalid refresh token", FailureReason.Unauthorized);
        }

        // Initiate lock and claim rotation
        // Both writes go under a single transaction to avoid failure in between leaving predecessor ReplaceById empty
        await using var tx = await unitOfWork.BeginTransactionAsync(ct);

        if (!await unitOfWork.RefreshSessions.TryMarkRotatedAsync(session.Id, now, ct))
        {
            // Lost race, another caller rotated this row. Roll back and take same path as a late replay
            // Find live successor and serve an access token from grace branch
            await tx.RollbackAsync(ct);
            return await HandleReplayAsync(session.Id, now, ct);
        }

        var (successor, token) = NewSession(session.UserId, userAgent);

        unitOfWork.RefreshSessions.Add(successor);
        await unitOfWork.CompleteAsync();
        await unitOfWork.RefreshSessions.SetReplacedByAsync(session.Id, successor.Id, ct);
        await tx.CommitAsync(ct);

        return Result<AuthResult>.Success(new AuthResult(
            user.ToDto(tokenService.CreateToken(user)), token, successor.ExpiresAt));
    }

    // Takes an id instead of entity. Can be reached in two ways: from token already revoked when it was read
    // and from losing the rotation race
    private async Task<Result<AuthResult>> HandleReplayAsync(int sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var session = await unitOfWork.RefreshSessions.ReloadAsync(sessionId, ct);

        // Gone or somehow not revoked. No cascade
        if (session?.RevokedAt is null)
            return Result<AuthResult>.Failure("Invalid refresh token", FailureReason.Unauthorized);

        var elapsed = now - session.RevokedAt.Value;

        // Measured from RevokedAt and never refreshed by replay, so window cannot be held by presenting token repeatedly
        var withinGrace =
            session.RevokedReason == SessionRevokedReason.Rotated &&
            elapsed < RotationGrace &&
            session.ReplacedBy is not null &&
            session.ReplacedBy.RevokedAt is null &&
            session.ReplacedBy.ExpiresAt > now;

        if (withinGrace)
        {
            var user = await userManager.FindByIdAsync(session.UserId);
            if (user is null)
                return Result<AuthResult>.Failure("Invalid refresh token", FailureReason.Unauthorized);

            logger.LogInformation(
                "Refresh token replayed {ElapsedMs}ms after rotation for user {UserId}; served inside grace window",
                elapsed.TotalMilliseconds, session.UserId);

            // Access token only. No rotation, nothing revoked, and no refresh token
            // Winning response already wrote live cookie into the jar this caller shares
            return Result<AuthResult>.Success(
                new AuthResult(user.ToDto(tokenService.CreateToken(user)), null, null));
        }

        // Logged-out or already cascaded token has no live chain. Revoking other sessions would punish them
        // for a race and protect nothing. 401 and log
        if (session.RevokedReason != SessionRevokedReason.Rotated)
        {
            logger.LogInformation(
                "Revoked ({Reason}) refresh token presented for user {UserId}",
                session.RevokedReason, session.UserId);

            return Result<AuthResult>.Failure("Invalid refresh token", FailureReason.Unauthorized);
        }

        // A rotated token replayed outside the window means a live chain exists and two parties hold credentials for it
        // Exactly one is legitimate and there is no way to tell which, to revoke everything and force authentication on both
        logger.LogWarning(
            "Refresh token reuse detected for user {UserId} (session {SessionId}, rotated {ElapsedMs}ms ago); revoking all sessions",
            session.UserId, session.Id, elapsed.TotalMilliseconds);

        await unitOfWork.RefreshSessions.RevokeAllForUserAsync(
            session.UserId, SessionRevokedReason.ReuseDetected, ct);

        return Result<AuthResult>.Failure("Invalid refresh token", FailureReason.Unauthorized);
    }

    private static Dictionary<string, string[]> MapIdentityErrors(IEnumerable<IdentityError> identityErrors)
    {
        return identityErrors
            .GroupBy(GetIdentityErrorField)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray());
    }

    private static string GetIdentityErrorField(IdentityError error)
    {
        if (error.Code.StartsWith("Password", StringComparison.OrdinalIgnoreCase))
            return "password";

        return error.Code switch
        {
            "DuplicateEmail" or "InvalidEmail" => "email",
            "DuplicateUserName" or "InvalidUserName" => "displayName",
            _ => "form"
        };
    }

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value.Substring(0, maxLength);
}
