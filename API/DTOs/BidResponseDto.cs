using System.ComponentModel.DataAnnotations;

namespace API.DTOs;

public class BidResponseDto
{
   public int BidId  { get; set; }
   public int AuctionId { get; set; }
   
   public decimal BidAmount { get; set; }
   public DateTimeOffset BidDate { get; set; }

   public string BidderId { get; set; } = "";
   public string BidderName { get; set; } = "";
   
}