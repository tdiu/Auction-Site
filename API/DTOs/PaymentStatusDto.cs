namespace API.DTOs;

public class PaymentStatusDto
{
    // Status is the effective status (Paid | Pending | Failed | Expired), folding in the latest
    // attempt. Payment.Status itself only knows Pending | Paid; see PaymentExtensions.ToEffectiveStatus.
    public required string Status { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
