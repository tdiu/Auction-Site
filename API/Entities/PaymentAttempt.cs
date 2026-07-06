using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Entities;

public class PaymentAttempt
{
    [Key]
    public int AttemptId { get; set; }
    public required int PaymentId { get; set; }
    public required decimal Amount { get; set; }
    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Pending;
    public string? StripeSessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey(nameof(PaymentId))] public Payment Payment { get; set; } = null!;
}
