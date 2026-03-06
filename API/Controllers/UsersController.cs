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
    [HttpGet("{id}")]
    public async Task<ActionResult<MemberDto>> GetUser(string id)
    {
        var user = await userRepository.GetUserByIdAsync(id);
        
        if (user == null) return NotFound();
        return user;
    }
}