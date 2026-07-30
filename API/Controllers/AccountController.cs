using API.DTOs;
using API.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

public class AccountController(IAuthService authService) : BaseApiController
{
    [HttpPost("register")] // api/account/register
    public async Task<ActionResult<UserDto>> Register(RegisterDto registerDto)
    {
        var result = await authService.RegisterAsync(registerDto);
        if (!result.IsSuccess)
            return HandleFailure(result);

        SetRefreshTokenCookie(
            result.Value!.RefreshToken,
            result.Value.RefreshTokenExpiry
        );
        return Ok(result.Value.User);
    }

    [HttpPost("login")] // api/account/login
    public async Task<ActionResult<UserDto>> Login(LoginDto loginDto)
    {
        var result = await authService.LoginAsync(loginDto);
        if (!result.IsSuccess)
            return HandleFailure(result);

        SetRefreshTokenCookie(
            result.Value!.RefreshToken,
            result.Value.RefreshTokenExpiry
        );
        return Ok(result.Value.User);
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        var result = await authService.LogoutAsync(refreshToken);

        DeleteRefreshTokenCookie();

        if (!result.IsSuccess)
            return HandleFailure(result);

        return NoContent();
    }

    [HttpPost("refresh-token")]
    public async Task<ActionResult<UserDto>> RefreshToken()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return NoContent();

        var result = await authService.RefreshTokenAsync(refreshToken);
        if (!result.IsSuccess)
            return HandleFailure(result);

        SetRefreshTokenCookie(result.Value!.RefreshToken, result.Value.RefreshTokenExpiry);
        return Ok(result.Value.User);
    }


    private void SetRefreshTokenCookie(string? refreshToken, DateTimeOffset? expires)
    {
        // Grace path returns an access token with no successor. The winning tab already wrote the
        // live cookie into the shared jar, so overwriting or clearing it here would break that tab
        if (string.IsNullOrEmpty(refreshToken) || expires is null) return;

        Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expires
        });
    }

    private void DeleteRefreshTokenCookie() => Response.Cookies.Delete("refreshToken");

}
