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

    public string PasswordResetUrl { get; set; } = "http://localhost:5173/reset-password";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(SenderAddress);
}
