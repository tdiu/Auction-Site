using System.ComponentModel.DataAnnotations.Schema;

namespace API.Entities;

public class Bid
{
    public int BidId { get; set; }
    public decimal BidAmount { get; set; }
    public DateTimeOffset BidDate { get; set; }
    public int AuctionId { get; set; }
    public string BidderId { get; set; }
    
    // nav prop
    [ForeignKey(nameof(AuctionId))]
    public Auction Auction { get; set; } = null!;

    [ForeignKey(nameof(BidderId))] 
    public AppUser Bidder { get; set; } = null!;
}