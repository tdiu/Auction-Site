using API.Entities;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class UserRepository(AppDbContext context) : IUserRepository
{
    public void Update(AppUser user)
    {
        context.Entry(user).State = EntityState.Modified;
    }

    public async Task<bool> SaveAllAsync()
    {
        return await context.SaveChangesAsync() > 0;
    }

    public async Task<IReadOnlyList<AppUser>> GetUsersAsync()
    {
        return await context.Users.ToListAsync();
    }

    public async Task<AppUser?> GetUserByIdAsync(string id)
    {
        return await context.Users.FindAsync(id);
    }
}