using System.Security.Cryptography;
using System.Text;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers;

public class AccountController(UserManager<AppUser> userManager, iTokenService tokenService) : BaseApiController
{
    [HttpPost("register")] // api/account/register
    public async Task<ActionResult<UserDto>> Register(RegisterDto registerDto)
    {
        if (await userManager.Users.AnyAsync(x => x.DisplayName == registerDto.DisplayName))
            return BadRequest("Username already exists");
        
        var user = new AppUser
        {
            DisplayName = registerDto.DisplayName,
            UserName = registerDto.DisplayName,
            Email = registerDto.Email,
            DateOfBirth = registerDto.DateOfBirth
        };
        
        var res = await userManager.CreateAsync(user, registerDto.Password);
        if (!res.Succeeded)
        {
            foreach (var error in res.Errors)
            {
                ModelState.AddModelError("identity", error.Description);
            }
            return ValidationProblem();
        }
        return user.ToDto(tokenService);
    }

    [HttpPost("login")] // api/account/login
    public async Task<ActionResult<UserDto>> Login(LoginDto loginDto)
    {
        var user = await userManager.FindByEmailAsync(loginDto.Email);
        
        if (user == null) return Unauthorized("Invalid Email");
        
        var res = await userManager.CheckPasswordAsync(user, loginDto.Password);
        
        if (!res) return Unauthorized("Invalid Password");

        return user.ToDto(tokenService);
    }
}