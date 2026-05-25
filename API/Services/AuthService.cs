using API.Core;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

public class AuthService(UserManager<AppUser> userManager, iTokenService tokenService) : IAuthService
{
    public async Task<Result<UserDto>> RegisterAsync(RegisterDto registerDto)
    {
        if (await userManager.Users.AnyAsync(x => x.DisplayName == registerDto.DisplayName))
            return Result<UserDto>.Failure("Username already exists");

        var user = new AppUser
        {
            DisplayName = registerDto.DisplayName,
            UserName = registerDto.DisplayName,
            Email = registerDto.Email,
            DateOfBirth = registerDto.DateOfBirth
        };

        var res = await userManager.CreateAsync(user, registerDto.Password);
        if (!res.Succeeded)
            return Result<UserDto>.Failure(
                string.Join("; ", res.Errors.Select(e => e.Description)));

        return Result<UserDto>.Success(user.ToDto(tokenService));
    }

    public async Task<Result<UserDto>> LoginAsync(LoginDto loginDto)
    {
        var user = await userManager.FindByEmailAsync(loginDto.Email);
        if (user == null) return Result<UserDto>.Failure("Invalid Email");

        var valid = await userManager.CheckPasswordAsync(user, loginDto.Password);
        if (!valid) return Result<UserDto>.Failure("Invalid Password");

        return Result<UserDto>.Success(user.ToDto(tokenService));
    }
}
