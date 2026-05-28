using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class RegisterDto
{
    [Required]
    [MinLength(3), MaxLength(15)]
    public string DisplayName { get; set; } = "";

    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [MinLength(4), MaxLength(15)]
    public string Password { get; set; } = "";

    [Required]
    public DateOnly DateOfBirth { get; set; }
}
