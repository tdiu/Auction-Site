using System.ComponentModel.DataAnnotations.Schema;

namespace API.Entities;

public class Auction
{
    public int AuctionId { get; set; }
    public required string ItemName { get; set; }
    public decimal StartingPrice { get; set; }
    public decimal? BuyNowPrice { get; set; }
    public string SellerId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    
    // navigation props
    [ForeignKey(nameof(SellerId))]
    public AppUser Seller { get; set; } = null!;
    
    
}