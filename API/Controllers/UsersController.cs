using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace API.Controllers;
public class UsersController(UserManager<AppUser> userManager) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> GetUsers()
    {
        var users = await userManager.Users.ToListAsync();
        var members = users.Select(u => u.ToMemberDto()).ToList();
        return Ok(members);
    }

    [Authorize]
    [HttpGet("{displayName}")]
    public async Task<ActionResult<MemberDto>> GetUser(string displayName)
    {
        var user = await userManager.FindByNameAsync(displayName);
        
        if (user == null) return NotFound();
        return user.ToMemberDto();
    }
}