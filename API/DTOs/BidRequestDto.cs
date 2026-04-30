using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class BidRequestDto
{
    [Required]
    public decimal Amount { get; set; }
}