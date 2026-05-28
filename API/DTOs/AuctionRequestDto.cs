using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class AuctionRequestDto
{
    [Required]
    [MinLength(3), MaxLength(30)]
    public required string ItemName { get; set; }
    [Required]
    [Range(0.1, 999999.99)]
    public decimal StartingPrice { get; set; } = 1;
    [Range(0.1, 999999.99)]
    public decimal? BuyNowPrice { get; set; }
}
