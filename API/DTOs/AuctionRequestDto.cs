using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class AuctionRequestDto
{
    [Required] public required string ItemName { get; set; }
    [Required] public decimal StartingPrice { get; set; } = 1;
    public decimal? BuyNowPrice { get; set; }
}