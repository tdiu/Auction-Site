using API.Entities;

namespace API.Interfaces;

public interface IUserRepository
{
    Task<AppUser> CreateUserAsync(AppUser user);
    void Update(AppUser user);
    Task<bool> SaveAllAsync();
    Task<IReadOnlyList<AppUser>> GetUsersAsync();
    Task<AppUser?> GetUserByIdAsync(string id);
    Task<bool> EmailExists(string email);
    Task<AppUser?> GetUserByEmailAsync(string email);
}