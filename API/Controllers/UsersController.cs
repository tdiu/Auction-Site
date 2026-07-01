using System.Security.Claims;
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
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> GetUsers([FromQuery] string? search)
    {
        var query = userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u => u.DisplayName.ToLower().Contains(term));

            // Don't offer the current user as a recipient to message.
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(currentUserId))
                query = query.Where(u => u.Id != currentUserId);

            query = query.OrderBy(u => u.DisplayName).Take(10);
        }

        var users = await query.ToListAsync();
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
