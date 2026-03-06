namespace API.DTOs;

public class MemberDto
{
    public string Id { get; set; }
    public string Email { get; set; }
    public string DisplayName { get; set; }
    public string? ImageUrl { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActive { get; set; }
    public string? Description { get; set; }
}