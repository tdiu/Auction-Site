using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class UserRepository(AppDbContext context) : IUserRepository
{
    public async Task<AppUser> CreateUserAsync(AppUser user)
    {
        context.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
    public void Update(AppUser user)
    {
        context.Entry(user).State = EntityState.Modified;
    }

    public async Task<bool> SaveAllAsync()
    {
        return await context.SaveChangesAsync() > 0;
    }

    public async Task<IReadOnlyList<MemberDto>> GetUsersAsync()
    {
        var users = await context.Users.ToListAsync();
        return users.Select(x => x.ToMemberDto()).ToList();
    }

    public async Task<MemberDto?> GetUserByIdAsync(string id)
    {
        var user = await context.Users.FindAsync(id);
        return user?.ToMemberDto();
    }

    public async Task<AppUser?> GetUserByEmailAsync(string email)
    {
        return await context.Users.FirstOrDefaultAsync(x => x.Email == email);
    }

    public async Task<bool> EmailExists(string email)
    {
        return await context.Users.AnyAsync(x => x.Email.ToLower() == email.ToLower());
    }

    public async Task<bool> DisplayNameExists(string displayName)
    {
        return await context.Users.AnyAsync(x => x.DisplayName.ToLower() == displayName.ToLower());
    }
}