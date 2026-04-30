using System.ComponentModel.DataAnnotations.Schema;

namespace API.Entities;

public class Bid
{
    public int BidId { get; set; }
    public required decimal BidAmount { get; set; }
    public required DateTimeOffset BidDate { get; set; }
    public required int AuctionId { get; set; }
    public required string BidderId { get; set; }
    
    // nav prop
    [ForeignKey(nameof(AuctionId))]
    public Auction Auction { get; set; } = null!;

    [ForeignKey(nameof(BidderId))] 
    public AppUser Bidder { get; set; } = null!;
}