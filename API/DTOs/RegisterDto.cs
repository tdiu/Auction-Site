using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class RegisterDto
{
    [Required] 
    public string DisplayName { get; set; } = "";

    [Required] 
    public string Email { get; set; } = "";

    [Required]
    [MinLength(4), MaxLength(15)]
    public string Password { get; set; } = "";
}