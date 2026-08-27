using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Email;

internal sealed class SmtpPasswordResetNotifier(
    IOptions<EmailSettings> options,
    ILogger<SmtpPasswordResetNotifier> logger) : IPasswordResetNotifier
{
    private readonly EmailSettings _settings = options.Value;

    public async Task SendAsync(
        string recipientEmail,
        string recipientName,
        string rawResetToken,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            logger.LogWarning(
                "Password reset email was not delivered because SMTP is not configured for {RecipientDomain}",
                GetDomain(recipientEmail));
            return;
        }

        var separator = _settings.PasswordResetUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var resetUrl = $"{_settings.PasswordResetUrl}{separator}token={Uri.EscapeDataString(rawResetToken)}";
        using var message = new MailMessage
        {
            From = new MailAddress(_settings.SenderAddress, _settings.SenderName),
            Subject = "AI-PMS password reset",
            Body = $"Hello {recipientName},\n\nReset your password using this link:\n{resetUrl}\n\nThis link expires at {expiresAtUtc:O} UTC.",
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipientEmail, recipientName));

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl
        };
        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException)
        {
            logger.LogError(
                exception,
                "Password reset email delivery failed for {RecipientDomain}",
                GetDomain(recipientEmail));
        }
    }

    private static string GetDomain(string email)
    {
        var separator = email.LastIndexOf('@');
        return separator >= 0 ? email[(separator + 1)..] : "unknown";
    }
}
