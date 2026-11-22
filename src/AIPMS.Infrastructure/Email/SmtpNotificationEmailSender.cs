using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIPMS.Application.Features.Notifications.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Email;

internal sealed partial class SmtpNotificationEmailSender(
    IOptions<EmailSettings> options,
    ILogger<SmtpNotificationEmailSender> logger,
    ISmtpTransport transport) : INotificationEmailSender
{
    private readonly EmailSettings settings = options.Value;

    public async Task<bool> TrySendAsync(NotificationEmailDelivery delivery, CancellationToken ct)
    {
        if (!settings.IsConfigured)
        {
            logger.LogWarning("Notification email is queued because SMTP is not configured for {RecipientDomain}.", Domain(delivery.Email));
            return false;
        }
        try
        {
            var plainBody = BuildPlainBody(delivery);
            var htmlBody = BuildHtmlBody(delivery);

            using var message = new MailMessage
            {
                From = new MailAddress(settings.SenderAddress, settings.SenderName),
                Subject = delivery.Subject,
                SubjectEncoding = Encoding.UTF8,
                Body = plainBody,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false
            };
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plainBody, Encoding.UTF8, MediaTypeNames.Text.Plain));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
            message.To.Add(new MailAddress(delivery.Email, delivery.RecipientName));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            await transport.SendAsync(settings, message, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Notification email timed out; remote delivery may be unknown.");
            return false;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogWarning("Notification email delivery failed for {RecipientDomain}.", Domain(delivery.Email));
            return false;
        }
    }

    internal static string BuildPlainBody(NotificationEmailDelivery delivery)
    {
        var sb = new StringBuilder();
        sb.Append("Xin chào ").Append(delivery.RecipientName).Append(",\n\n");
        sb.Append(delivery.Body);
        if (IsSafeActionUrl(delivery.ActionUrl)
            && !delivery.Body.Contains(delivery.ActionUrl!, StringComparison.OrdinalIgnoreCase))
        {
            var label = string.IsNullOrWhiteSpace(delivery.ActionLabel) ? "Mở trên AI-PMS" : delivery.ActionLabel.Trim();
            sb.Append("\n\n").Append(label).Append(": ").Append(delivery.ActionUrl!.Trim());
        }
        sb.Append("\n\n---\nEmail này được gửi tự động từ hệ thống quản lý đồ án AI-PMS.");
        return sb.ToString();
    }

    internal static string BuildHtmlBody(NotificationEmailDelivery delivery)
    {
        var safeRecipient = HtmlEscape(delivery.RecipientName);
        var safeSubject = HtmlEscape(delivery.Subject);
        var renderedLines = FormatHtmlParagraphs(delivery.Body);

        var ctaBlock = string.Empty;
        if (IsSafeActionUrl(delivery.ActionUrl))
        {
            var safeUrl = HtmlEscape(delivery.ActionUrl!.Trim());
            var safeLabel = HtmlEscape(string.IsNullOrWhiteSpace(delivery.ActionLabel) ? "Mở trên AI-PMS" : delivery.ActionLabel.Trim());
            ctaBlock =
                "<div style=\"margin:24px 0 16px;\">" +
                $"<a href=\"{safeUrl}\" target=\"_blank\" rel=\"noopener noreferrer\" " +
                "style=\"display:inline-block;background:#c2410c;color:#ffffff;text-decoration:none;font-weight:700;font-size:14px;padding:12px 22px;border-radius:8px;\">" +
                $"{safeLabel}</a></div>" +
                "<p style=\"margin:0 0 8px;font-size:12px;color:#64748b;line-height:1.5;\">" +
                $"Nếu nút không hoạt động, hãy nhấn hoặc sao chép liên kết sau:<br/>" +
                $"<a href=\"{safeUrl}\" target=\"_blank\" rel=\"noopener noreferrer\" style=\"color:#1d4ed8;word-break:break-all;\">{safeUrl}</a></p>";
        }

        return
            "<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"UTF-8\"/></head>" +
            "<body style=\"margin:0;padding:24px 12px;background:#f1f5f9;font-family:'Segoe UI',Arial,sans-serif;color:#0f172a;\">" +
            "<div style=\"max-width:600px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;overflow:hidden;\">" +
            "<div style=\"background:linear-gradient(135deg,#0f172a,#1e3a8a);padding:18px 24px;color:#ffffff;\">" +
            "<div style=\"font-size:12px;letter-spacing:0.08em;text-transform:uppercase;color:#93c5fd;font-weight:700;\">AI-PMS · Hệ thống Quản lý Đồ án</div>" +
            $"<h1 style=\"margin:6px 0 0;font-size:18px;line-height:1.35;font-weight:700;color:#ffffff;\">{safeSubject}</h1>" +
            "</div>" +
            "<div style=\"padding:24px;font-size:14px;line-height:1.65;color:#1e293b;\">" +
            $"<p style=\"margin:0 0 14px;\">Xin chào <strong>{safeRecipient}</strong>,</p>" +
            renderedLines +
            ctaBlock +
            "</div>" +
            "<div style=\"padding:14px 24px;background:#f8fafc;border-top:1px solid #e2e8f0;font-size:12px;color:#64748b;\">" +
            "Email này được gửi tự động từ hệ thống quản lý đồ án <strong>AI-PMS</strong>. Vui lòng không trả lời trực tiếp email này." +
            "</div></div></body></html>";
    }

    private static string HtmlEscape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#39;");

    private static string FormatHtmlParagraphs(string rawBody)
    {
        var lines = rawBody.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                sb.Append("<div style=\"height:8px;\"></div>");
                continue;
            }
            var encoded = LinkifyUrls(line);
            if (line.StartsWith('•') || line.StartsWith('-'))
            {
                sb.Append("<p style=\"margin:4px 0;padding-left:6px;color:#334155;\">").Append(encoded).Append("</p>");
            }
            else
            {
                sb.Append("<p style=\"margin:0 0 10px;\">").Append(encoded).Append("</p>");
            }
        }
        return sb.ToString();
    }

    private static string LinkifyUrls(string rawLine)
    {
        var sb = new StringBuilder();
        var lastIndex = 0;
        foreach (Match match in UrlRegex().Matches(rawLine))
        {
            if (match.Index > lastIndex)
                sb.Append(HtmlEscape(rawLine[lastIndex..match.Index]));
            var rawUrl = match.Value.TrimEnd('.', ',', ';', ')');
            var trailing = match.Value[rawUrl.Length..];
            var safeUrl = HtmlEscape(rawUrl);
            sb.Append($"<a href=\"{safeUrl}\" target=\"_blank\" rel=\"noopener noreferrer\" style=\"color:#1d4ed8;text-decoration:underline;word-break:break-all;\">{safeUrl}</a>");
            if (trailing.Length > 0) sb.Append(HtmlEscape(trailing));
            lastIndex = match.Index + match.Length;
        }
        if (lastIndex < rawLine.Length)
            sb.Append(HtmlEscape(rawLine[lastIndex..]));
        return sb.ToString();
    }

    private static bool IsSafeActionUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string Domain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 ? email[(at + 1)..] : "unknown";
    }

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();
}
