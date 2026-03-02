using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class AuctionRequestDto
{
    [Required] public string ItemName { get; set; } = "";
    [Required] public decimal StartingPrice { get; set; } = 0;
    public decimal? BuyNowPrice { get; set; }
}