namespace API.DTOs;

public class PaymentStatusDto
{
    public required string Status { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
