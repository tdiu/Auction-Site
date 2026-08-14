using System.Security.Claims;
using API.Core;
using API.DTOs;
using API.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

public class AccountController(IAuthService authService,
    IAuthenticationSchemeProvider schemeProvider,
    IConfiguration config) : BaseApiController
{
    private static readonly Dictionary<string, string> ExternalProviders =
        new(StringComparer.OrdinalIgnoreCase) { ["google"] = "Google" };

    [EnableRateLimiting("auth")]
    [HttpPost("register")] // api/account/register
    public async Task<ActionResult<UserDto>> Register(RegisterDto registerDto)
    {
        var result = await authService.RegisterAsync(registerDto, Request.Headers.UserAgent);
        if (!result.IsSuccess)
            return HandleFailure(result);

        SetRefreshTokenCookie(
            result.Value!.RefreshToken,
            result.Value.RefreshTokenExpiry
        );
        return Ok(result.Value.User);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")] // api/account/login
    public async Task<ActionResult<UserDto>> Login(LoginDto loginDto)
    {
        var result = await authService.LoginAsync(loginDto, Request.Headers.UserAgent);
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

        var result = await authService.RefreshTokenAsync(refreshToken, Request.Headers.UserAgent);

        if (!result.IsSuccess)
        {
            // Cookie is expired, revoked, or cascaded
            // Leaving it means every subsequent boot replays and re-triggers the same path
            if (result.Reason == FailureReason.Unauthorized)
                DeleteRefreshTokenCookie();
            return HandleFailure(result);
        }

        // Both null on the grace path, which SetRefreshTokenCookie treats as "leave the jar alone"
        SetRefreshTokenCookie(result.Value!.RefreshToken, result.Value.RefreshTokenExpiry);
        return Ok(result.Value.User);
    }

    [HttpGet("external-login/{provider}")]
    public async Task<IActionResult> ExternalLogin(string provider, string? returnUrl)
    {
        if (!ExternalProviders.TryGetValue(provider, out var scheme) ||
            await schemeProvider.GetSchemeAsync(scheme) is null)
            return NotFound();

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(ExternalLoginCallback), new { provider })!
        };
        properties.Items["returnUrl"] = SafeReturnUrl(returnUrl);
        return Challenge(properties, scheme);
    }

    [HttpGet("external-login/{provider}/callback")]
    public async Task<IActionResult> ExternalLoginCallback(string provider)
    {
        if (!ExternalProviders.TryGetValue(provider, out var scheme))
            return NotFound();

        var external = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!external.Succeeded)
            return RedirectToClient("/login", "external_failed", scheme);

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        var providerKey = external.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(providerKey))
            return RedirectToClient("/login", "external_failed", scheme);

        var email = external.Principal.FindFirstValue(ClaimTypes.Email);

        var emailVerified = external.Principal.FindFirstValue("email_verified");
        if (!string.IsNullOrWhiteSpace(email) &&
            !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
            return RedirectToClient("/login", "email_unverified", scheme);

        var result = await authService.ExternalLoginAsync(
            new ExternalLoginRequest(
                scheme,
                providerKey,
                email,
                external.Principal.FindFirstValue(ClaimTypes.Name)),
                Request.Headers.UserAgent);

        if (!result.IsSuccess)
            return RedirectToClient("/login", result.Reason switch
            {
                FailureReason.Conflict => "email_has_password",
                FailureReason.Validation => "no_email",
                _ => "external_failed"
            }, scheme);

        var auth = result.Value!;
        if (auth.RefreshToken is null || auth.RefreshTokenExpiry is null)
            return RedirectToClient("/login", "external_failed", scheme);

        SetRefreshTokenCookie(auth.RefreshToken, auth.RefreshTokenExpiry.Value);
        return RedirectToClient(SafeReturnUrl(external.Properties?.GetString("returnUrl")));
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

    private static string SafeReturnUrl(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw[0] != '/')
            return "/";
        if (raw.StartsWith("//") || raw.StartsWith("/\\"))
            return "/";
        return raw;
    }

    private IActionResult RedirectToClient(string path, string? reason = null, string? provider = null)
    {
        var clientUrl = (config["ClientAppUrl"] ?? "").TrimEnd('/');
        var query = reason is null
            ? ""
            : $"?error={Uri.EscapeDataString(reason)}" +
              (provider is null ? "" : $"&provider={Uri.EscapeDataString(provider)}");
        return Redirect($"{clientUrl}{path}{query}");
    }

}
