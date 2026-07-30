using API.Core;
using API.DTOs;

namespace API.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResult>> RegisterAsync(RegisterDto registerDto, string? userAgent = null);
    Task<Result<AuthResult>> LoginAsync(LoginDto loginDto, string? userAgent = null);
    Task<Result<AuthResult>> RefreshTokenAsync(string refreshToken, string? userAgent = null, CancellationToken ct = default);
    Task<Result<bool>> LogoutAsync(string? refreshToken, CancellationToken ct = default);
}
