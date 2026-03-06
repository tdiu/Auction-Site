using API.DTOs;
using API.Entities;
using API.Interfaces;

namespace API.Extensions;

public static class AppUserExtensions
{
    public static UserDto ToDto(this AppUser user, iTokenService tokenService)
    {
        return new UserDto
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Token = tokenService.CreateToken(user)
        };
    }

    public static MemberDto ToMemberDto(this AppUser user)
    {
        return new MemberDto
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            ImageUrl = user.ImageUrl,
            DateOfBirth = user.DateOfBirth,
            CreatedAt = user.CreatedAt,
            LastActive = user.LastActive,
            Description = user.Description
        };
    }
}