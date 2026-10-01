using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AIPMS.Application.Features.Auth.Abstractions;

namespace AIPMS.Infrastructure.Email;

internal static class PasswordRecoveryRegistration
{
    public static IServiceCollection AddPasswordRecovery(this IServiceCollection services)
    {
        services.AddOptions<PasswordRecoverySettings>().BindConfiguration("PasswordRecovery")
            .Validate(s => s.IntervalSeconds is >= 1 and <= 60 && s.BatchSize is >= 1 and <= 100
                && s.LeaseSeconds is >= 60 and <= 600 && s.MaxAttempts is >= 1 and <= 10, "Invalid password recovery worker limits.")
            .ValidateOnStart();
        services.AddOptions<PasswordRecoverySettings>().Configure<IConfiguration, IHostEnvironment>((s, config, env) =>
        {
            if (!s.Enabled) return;
            var timeout = int.TryParse(config["Email:TimeoutSeconds"], out var seconds) ? seconds : 30;
            s.IsReady = s.LookupKey.Length >= 32 && System.IO.Path.IsPathFullyQualified(s.KeyRingPath)
                && Uri.TryCreate(config["Email:PasswordResetUrl"], UriKind.Absolute, out var url)
                && (url.Scheme == "https" || (env.IsDevelopment() && url.Scheme == "http" && url.IsLoopback))
                && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment) && string.IsNullOrEmpty(url.UserInfo)
                && s.LeaseSeconds > timeout + 15 && timeout > 0
                && !string.IsNullOrWhiteSpace(config["Email:Host"])
                && !string.IsNullOrWhiteSpace(config["Email:Username"])
                && !string.IsNullOrWhiteSpace(config["Email:Password"])
                && System.Net.Mail.MailAddress.TryCreate(config["Email:SenderAddress"], out _)
                && bool.TryParse(config["Email:EnableSsl"], out var tls) && tls;
        });
        services.AddDataProtection().SetApplicationName("AI-PMS.PasswordRecovery");
        services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory>((options, config, logging) =>
        {
            var path = config["PasswordRecovery:KeyRingPath"];
            if (!string.IsNullOrWhiteSpace(path))
                options.XmlRepository = new FileSystemXmlRepository(new System.IO.DirectoryInfo(path), logging);
        });
        services.AddScoped<IPasswordRecoveryQueue, PasswordRecoveryQueue>();
        return services;
    }
}
