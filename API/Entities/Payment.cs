using System.ComponentModel.DataAnnotations.Schema;

namespace API.Entities;

public class Payment
{
    public int PaymentId { get; set; }
    public required int AuctionId { get; set; }
    public required string UserId { get; set; }
    public required decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public ICollection<PaymentAttempt> Attempts { get; set; } = new List<PaymentAttempt>();

    // nav props
    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    [ForeignKey(nameof(AuctionId))]
    public Auction Auction { get; set; } = null!;

    // Write-once guarded transition. The only sanctioned way to set Paid, so stray
    // code can't flip Status without going through here. Idempotent (webhook redelivery-safe).
    public void MarkPaid(DateTimeOffset completedAt)
    {
        if (Status == PaymentStatus.Paid) return;
        Status = PaymentStatus.Paid;
        CompletedAt = completedAt;
    }

}
