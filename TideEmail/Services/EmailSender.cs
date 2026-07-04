using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace TideEmail.Services;

/// <summary>Sends the report through Gmail SMTP with STARTTLS.</summary>
internal static class EmailSender
{
    internal static async Task Send(string subject, string html, string text,
                                    string sender, string password, string recipient)
    {
        var recipients = ParseRecipients(recipient);

        var msg = new MimeMessage();
        msg.Subject = subject;
        msg.From.Add(MailboxAddress.Parse(sender));
        msg.To.Add(MailboxAddress.Parse(sender)); // header only — actual delivery below
        msg.Body = new BodyBuilder { TextBody = text, HtmlBody = html }.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
        await smtp.AuthenticateAsync(sender, password);
        // Envelope recipients are the parsed list (BCC-style), matching the original smtplib behavior.
        await smtp.SendAsync(msg, MailboxAddress.Parse(sender), recipients.Select(MailboxAddress.Parse));
        await smtp.DisconnectAsync(true);
    }

    private static List<string> ParseRecipients(string raw) =>
        raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .ToList();
}
