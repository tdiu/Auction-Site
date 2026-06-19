using API.Core;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

public class AuthService(UserManager<AppUser> userManager, ITokenService tokenService) : IAuthService
{
    public async Task<Result<AuthResult>> RegisterAsync(RegisterDto registerDto)
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

        return await IssueAuthTokenAsync(user);
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginDto loginDto)
    {
        var email = loginDto.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
            return Result<AuthResult>.Failure("Invalid Credentials", FailureReason.Unauthorized);

        var valid = await userManager.CheckPasswordAsync(user, loginDto.Password);
        if (!valid)
            return Result<AuthResult>.Failure("Invalid Credentials", FailureReason.Unauthorized);

        return await IssueAuthTokenAsync(user);
    }

    public async Task<Result<AuthResult>> RefreshTokenAsync(string refreshToken)
    {
        var user = await userManager.Users
            .FirstOrDefaultAsync(x =>
                x.RefreshToken == refreshToken &&
                x.RefreshTokenExpiry > DateTime.UtcNow);
        if (user == null)
            return Result<AuthResult>.Failure("Invalid refresh token", FailureReason.Unauthorized);

        return await IssueAuthTokenAsync(user);
    }

    public async Task<Result<bool>> LogoutAsync(string? refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return Result<bool>.Success(true);

        var user = await userManager.Users
            .FirstOrDefaultAsync(x => x.RefreshToken == refreshToken);

        if (user == null)
            return Result<bool>.Success(true);

        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        var result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
            return Result<bool>.Failure("Failed to log out", FailureReason.InternalError);

        return Result<bool>.Success(true);
    }

    private async Task<Result<AuthResult>> IssueAuthTokenAsync(AppUser user)
    {
        var refreshToken = tokenService.GenerateRefreshToken();
        var refreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = refreshTokenExpiry;

        var updateResult = await userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
            return Result<AuthResult>.ValidationFailure(MapIdentityErrors(updateResult.Errors));

        var authResult = new AuthResult(
            user.ToDto(tokenService),
            refreshToken,
            refreshTokenExpiry
        );
        return Result<AuthResult>.Success(authResult);
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
}
