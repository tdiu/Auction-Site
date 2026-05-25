using System.ComponentModel.DataAnnotations;
using API.Entities;

namespace API.DTOs;

public class AuctionResponseDto
{
    [Required]
    public int AuctionId { get; set; }
    [Required] public string ItemName { get; set; } = "";
    [Required] public decimal StartingPrice { get; set; } = 0;
    public decimal? BuyNowPrice { get; set; }
    public decimal? CurrentHighBid  { get; set; }
    public string? CurrentHighBidderId { get; set; }
    [Required]
    public string SellerId { get; set; } = "";
    [Required]
    public string SellerName { get; set; } = "";
    public DateTimeOffset SellerCreatedAt { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    
    public AuctionStatus Status { get; set; }
}