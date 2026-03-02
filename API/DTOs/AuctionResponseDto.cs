using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class AuctionResponseDto
{
    [Required]
    public int AuctionId { get; set; }
    [Required] public string ItemName { get; set; } = "";
    [Required] public decimal StartingPrice { get; set; } = 0;
    public decimal? BuyNowPrice { get; set; }
    [Required]
    public string SellerId { get; set; } = "";
    public string SellerName { get; set; } = "";
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}