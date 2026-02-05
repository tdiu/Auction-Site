using API.Data;
using API.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UsersController(AppDbContext context) : Controller
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppUser>>> GetUsers()
    {
        var users = await context.Users.ToListAsync();
        return users;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AppUser>> GetUser(string id)
    {
        var user = await context.Users.FindAsync(id);
        
        if (user == null) return NotFound();
        return user;
    }
}