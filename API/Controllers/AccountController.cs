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
        return result.IsSuccess ? result.Value : BadRequest(result.Error);
    }

    [HttpPost("login")] // api/account/login
    public async Task<ActionResult<UserDto>> Login(LoginDto loginDto)
    {
        var result = await authService.LoginAsync(loginDto);
        return result.IsSuccess ? result.Value : Unauthorized(result.Error);
    }
}
