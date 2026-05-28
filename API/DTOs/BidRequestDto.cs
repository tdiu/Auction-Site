using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class BidRequestDto
{
    [Required]
    [Range(0.1, 999999.99)]
    public decimal Amount { get; set; }
}
