using API.Core;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace API.Services;

public class AuthService(
    UserManager<AppUser> userManager,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    ILogger<AuthService> logger) : IAuthService
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
            DisplayName = displayName, UserName = displayName, Email = email, DateOfBirth = registerDto.DateOfBirth
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

    public async Task<Result<AuthResult>> ExternalLoginAsync(
        ExternalLoginRequest request, string? userAgent = null, CancellationToken ct = default)
    {
        var existing = await userManager.FindByLoginAsync(request.Provider, request.ProviderKey);
        if (existing != null)
        {
            if (await userManager.IsLockedOutAsync(existing))
            {
                logger.LogWarning(
                    "Locked out user {UserId} attempted {Provider} sign-in", existing.Id, request.Provider);

                return Result<AuthResult>.Failure("This account is locked", FailureReason.Unauthorized);
            }

            return await IssueAuthTokenAsync(existing, userAgent);
        }

        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return Result<AuthResult>.Failure($"{request.Provider} did not supply an email address",
                FailureReason.Validation);

        // One email, one account, one auth method. No linking in either direction
        if (await userManager.FindByEmailAsync(email) != null)
            return Result<AuthResult>.Failure("Email is already taken", FailureReason.Conflict);

        var created = await CreateExternalUserAsync(email, request, ct);
        if (!created.IsSuccess)
            return new Result<AuthResult>
            {
                IsSuccess = false,
                Error = created.Error,
                ValidationErrors = created.ValidationErrors,
                Reason = created.Reason
            };
        return await IssueAuthTokenAsync(created.Value!, userAgent);
    }

    public async Task<Result<bool>> LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return Result<bool>.Success(true);

        var session =
            await unitOfWork.RefreshSessions.GetByTokenHashAsync(tokenService.HashRefreshToken(refreshToken), ct);

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

        return Result<AuthResult>.Success(new AuthResult(await BuildUserDtoAsync(user), refreshToken,
            session.ExpiresAt));
    }

    // AspNetUserLogins is the source of truth for how an account signs in, so the provider is read
    // rather than inferred from a null PasswordHash. Without account linking a user has at most one,
    // and no row at all means they registered with a password.
    private async Task<UserDto> BuildUserDtoAsync(AppUser user)
    {
        var provider = (await userManager.GetLoginsAsync(user)).FirstOrDefault()?.LoginProvider;

        return user.ToDto(tokenService.CreateToken(user), provider);
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
    public async Task<Result<AuthResult>> RefreshTokenAsync(string refreshToken, string? userAgent = null,
        CancellationToken ct = default)
    {
        var session =
            await unitOfWork.RefreshSessions.GetByTokenHashAsync(tokenService.HashRefreshToken(refreshToken), ct);

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
            await BuildUserDtoAsync(user), token, successor.ExpiresAt));
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
                new AuthResult(await BuildUserDtoAsync(user), null, null));
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

    private async Task<Result<AppUser>> CreateExternalUserAsync(string email, ExternalLoginRequest request,
        CancellationToken ct)
    {
        var baseName = DisplayNameGenerator.Derive(request.Name, email);
        var user = new AppUser { Email = email, EmailConfirmed = true, DisplayName = baseName, };

        // Account row and provider link must land together. A user with no password nor login row
        // is unusable and its email blocks both signup paths forever
        await using var tx = await unitOfWork.BeginTransactionAsync(ct);

        for (var attempt = 0; attempt < DisplayNameGenerator.MaxAttempts; attempt++)
        {
            var candidate = DisplayNameGenerator.Candidate(baseName, attempt);
            user.DisplayName = candidate;
            user.UserName = candidate;

            // Failed INSERT aborts the enclosing Postgres transaction. Every attempt runs inside its own
            // savepoint. Rolling back to it leaves the entity Added in the change tracker
            await tx.CreateSavepointAsync("attempt", ct);

            IdentityResult result;
            try
            {
                result = await userManager.CreateAsync(user);
            }
            catch (DbUpdateException ex) when (IsNameCollision(ex))
            {
                await tx.RollbackToSavepointAsync("attempt", ct);
                continue;
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                // EmailIndex is unique. Concurrent signup for the same email lands here
                // Should not retry
                return Result<AppUser>.Failure("Email already registered", FailureReason.Conflict);
            }

            if (result.Succeeded)
            {
                try
                {
                    var link = await userManager.AddLoginAsync(user, new UserLoginInfo(
                        request.Provider, request.ProviderKey, request.Provider));

                    if (!link.Succeeded)
                    {
                        logger.LogError(
                            "Could not link {Provider} login for {Email}: {Errors}",
                            request.Provider, email, string.Join("; ", link.Errors.Select(e => e.Code)));

                        return Result<AppUser>.Failure("Could not complete sign-in", FailureReason.InternalError);
                    }
                }
                catch (DbUpdateException ex)
                {
                    // Transaction rolls back on dispose and takes account row with it
                    // No orphan to recover from; user just retries
                    logger.LogError(ex,
                        "Could not link {Provider} login for {Email}", request.Provider, email);

                    return Result<AppUser>.Failure("Could not complete sign-in", FailureReason.InternalError);
                }

                await tx.CommitAsync(ct);
                return Result<AppUser>.Success(user);
            }

            // Validator caught the duplicate before INSERT
            if (result.Errors.Any(e => e.Code == "DuplicateUserName"))
            {
                await tx.RollbackToSavepointAsync("attempt", ct);
                continue;
            }

            return Result<AppUser>.ValidationFailure(MapIdentityErrors(result.Errors));
        }

        // Unreachable in practice: the last attempt takes Candidate's GUID branch, which cannot
        // collide. Reaching here means that branch stopped being reachable, not that we were unlucky
        logger.LogError(
            "Exhausted {Attempts} display name attempts from base {BaseName} for {Email}",
            DisplayNameGenerator.MaxAttempts, baseName, email);

        return Result<AppUser>.Failure("Could not allocate a username", FailureReason.InternalError);
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

    private static bool IsNameCollision(DbUpdateException ex)
        => ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_AspNetUsers_DisplayName" or "UserNameIndex"
        };

}
