using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class CreateMessageDto
{
    [Required]
    public required string RecipientId { get; set; }
    [Required]
    [MinLength(1), MaxLength(200)]
    public required string Content { get; set; }
}
