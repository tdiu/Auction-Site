using API.Core;
using API.DTOs;

namespace API.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResult>> RegisterAsync(RegisterDto registerDto);
    Task<Result<AuthResult>> LoginAsync(LoginDto loginDto);
    Task<Result<AuthResult>> RefreshTokenAsync(string refreshToken);
}
