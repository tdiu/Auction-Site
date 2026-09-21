namespace API.Interfaces;

public interface IEmailSender
{
    /// <summary>
    /// Outbox delivery is at-least-once. idempotencyKey names the logical send so a provider that
    /// supports idempotent requests can collapse repeats. MailKit cannot: SMTP has no such mechanism.
    /// </summary>
    Task SendAsync(string toEmail, string subject, string htmlBody, string idempotencyKey, CancellationToken ct);
}
