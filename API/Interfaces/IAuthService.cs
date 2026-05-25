using API.Core;
using API.DTOs;

namespace API.Interfaces;

public interface IAuthService
{
    Task<Result<UserDto>> RegisterAsync(RegisterDto registerDto);
    Task<Result<UserDto>> LoginAsync(LoginDto loginDto);
}
