
using System.Net;
using API.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace API.Services.Email;

public class MailKitEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _o = options.Value;

    public async Task SendAsync(string toEmail, string subject, string htmlBody, string idempotencyKey,
        CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, _o.FromAddress));
        msg.To.Add(MailboxAddress.Parse(toEmail));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();
        msg.MessageId = idempotencyKey;

        using var client = new SmtpClient();
        var security = _o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(_o.Host, _o.Port, security, ct);

        if (!string.IsNullOrEmpty(_o.Username) && !string.IsNullOrEmpty(_o.Password))
            await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}
