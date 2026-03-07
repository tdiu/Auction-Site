using API.DTOs;
using API.Entities;

namespace API.Interfaces;

public interface IUserRepository
{
    Task<AppUser> CreateUserAsync(AppUser user);
    void Update(AppUser user);
    Task<bool> SaveAllAsync();
    Task<IReadOnlyList<MemberDto>> GetUsersAsync();
    Task<MemberDto?> GetUserByDisplayNameAsync(string id);
    Task<bool> EmailExists(string email);
    Task<bool> DisplayNameExists(string displayName);
    Task<AppUser?> GetUserByEmailAsync(string email);
}