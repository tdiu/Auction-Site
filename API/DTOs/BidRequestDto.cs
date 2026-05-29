using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class BidRequestDto
{
    [Required]
    [Range(0.1, double.MaxValue, ErrorMessage = "The amount must be greater than 0.")]
    public decimal Amount { get; set; }
}
