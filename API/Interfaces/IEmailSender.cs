namespace API.Interfaces;

public interface IEmailSender
{
    /// <summary>
    /// idempotencyKey lets HTTP provider dedupe when outbox redelivers the same AuctionEnded row (at-least-once)
    /// </summary>
    Task SendAsync(string toEmail, string subject, string htmlBody, string idempotencyKey, CancellationToken ct);
}
