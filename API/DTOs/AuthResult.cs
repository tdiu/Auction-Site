using API.DTOs;

namespace API.Core;

public record AuthResult(
    UserDto User,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiry
    );
