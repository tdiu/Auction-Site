using System.ComponentModel.DataAnnotations.Schema;

namespace API.Entities;

public class Payment
{
    public int PaymentId { get; set; }
    public required int AuctionId { get; set; }
    public required string UserId { get; set; }
    public required decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } =  PaymentStatus.Pending;
    public string? StripeSessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // nav props
    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    [ForeignKey(nameof(AuctionId))]
    public  Auction Auction { get; set; } = null!;

}
