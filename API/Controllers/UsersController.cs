using API.DTOs;
using API.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;
public class UsersController(IUserRepository userRepository) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> GetUsers()
    {
        return Ok(await userRepository.GetUsersAsync());
    }

    [Authorize]
    [HttpGet("{displayName}")]
    public async Task<ActionResult<MemberDto>> GetUser(string displayName)
    {
        var user = await userRepository.GetUserByDisplayNameAsync(displayName);
        
        if (user == null) return NotFound();
        return user;
    }
}