using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Email;

internal sealed class SmtpPasswordResetNotifier(
    IOptions<EmailSettings> options,
    ILogger<SmtpPasswordResetNotifier> logger, ISmtpTransport transport) : IPasswordResetNotifier
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

        try
        {
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
            await transport.SendAsync(_settings, message, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Password reset email timed out; remote delivery may be unknown.");
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogError("Password reset email delivery failed for {RecipientDomain}", GetDomain(recipientEmail));
        }
    }

    private static string GetDomain(string email)
    {
        var separator = email.LastIndexOf('@');
        return separator >= 0 ? email[(separator + 1)..] : "unknown";
    }
}
