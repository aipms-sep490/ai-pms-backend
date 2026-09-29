using System.Net.Mail;
using AIPMS.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Email;

internal sealed class IntegrationConfigurationValidator(IConfiguration config) : IValidateOptions<EmailSettings>
{
    public ValidateOptionsResult Validate(string? name, EmailSettings settings)
    {
        if (string.Equals(config["FileStorage:Provider"], "GoogleDrive", StringComparison.OrdinalIgnoreCase)
            && !GoogleDriveSettings.Read(config).IsValid)
            return ValidateOptionsResult.Fail("GoogleDrive requires credentials, FolderId and a valid timeout.");
        var notifications = bool.TryParse(config["NotificationEmail:Enabled"], out var enabled) && enabled;
        if (!notifications && !settings.IsConfigured && string.IsNullOrWhiteSpace(settings.Username)
            && string.IsNullOrWhiteSpace(settings.Password) && string.IsNullOrWhiteSpace(settings.Host)
            && string.IsNullOrWhiteSpace(settings.SenderAddress)) return ValidateOptionsResult.Success;
        if (string.IsNullOrWhiteSpace(settings.Host) || settings.Host.Any(char.IsWhiteSpace)
            || !MailAddress.TryCreate(settings.SenderAddress, out var address) || address.Address != settings.SenderAddress
            || !settings.EnableSsl || string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password))
            return ValidateOptionsResult.Fail("SMTP requires host, TLS, sender address, username and password when configured or enabled.");
        return ValidateOptionsResult.Success;
    }
}
