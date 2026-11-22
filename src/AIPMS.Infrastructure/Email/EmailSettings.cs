namespace AIPMS.Infrastructure.Email;

public sealed class EmailSettings
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string SenderAddress { get; set; } = string.Empty;

    public string SenderName { get; set; } = "AI-PMS";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    public string PasswordResetUrl { get; set; } = "http://localhost:5173/reset-password";

    public string FrontendBaseUrl { get; set; } = string.Empty;

    public string ResolveFrontendBaseUrl()
    {
        if (Uri.TryCreate(FrontendBaseUrl, UriKind.Absolute, out var explicitUri)
            && (explicitUri.Scheme == Uri.UriSchemeHttp || explicitUri.Scheme == Uri.UriSchemeHttps))
        {
            return explicitUri.GetLeftPart(UriPartial.Authority);
        }

        if (Uri.TryCreate(PasswordResetUrl, UriKind.Absolute, out var resetUri)
            && (resetUri.Scheme == Uri.UriSchemeHttp || resetUri.Scheme == Uri.UriSchemeHttps))
        {
            return resetUri.GetLeftPart(UriPartial.Authority);
        }

        return "http://localhost:5173";
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(SenderAddress);
}
