namespace API.DTOs;

public class MemberDto
{
    public required string Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? ImageUrl { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActive { get; set; }
    public string? Description { get; set; }
}