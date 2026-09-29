using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Email;

internal interface ISmtpTransport
{
    Task SendAsync(EmailSettings settings, MailMessage message, CancellationToken ct);
}

internal sealed class SmtpTransport : ISmtpTransport
{
    public async Task SendAsync(EmailSettings settings, MailMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient(settings.Host, settings.Port)
        { EnableSsl = settings.EnableSsl, Timeout = settings.TimeoutSeconds * 1000 };
        if (!string.IsNullOrWhiteSpace(settings.Username)) client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        await client.SendMailAsync(message, ct);
    }
}
