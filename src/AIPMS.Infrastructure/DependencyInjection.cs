using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Email;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Infrastructure.Email;
using AIPMS.Infrastructure.Identity;
using AIPMS.Infrastructure.Identity.Configuration;
using AIPMS.Infrastructure.Persistence.Configuration;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<DatabaseSettings>()
            .Configure<IConfiguration>(static (settings, configuration) =>
            {
                settings.DefaultConnection =
                    configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
            })
            .Validate(
                static settings => !string.IsNullOrWhiteSpace(settings.DefaultConnection),
                "DefaultConnection is not configured.")
            .ValidateOnStart();

        services.AddOptions<JwtSettings>()
            .Configure<IConfiguration>(static (settings, configuration) =>
            {
                settings.Issuer = configuration["Jwt:Issuer"] ?? string.Empty;
                settings.Audience = configuration["Jwt:Audience"] ?? string.Empty;
                settings.SigningKey = configuration["Jwt:SigningKey"] ?? string.Empty;
                settings.AccessTokenMinutes = int.TryParse(
                    configuration["Jwt:AccessTokenMinutes"],
                    out var minutes)
                    ? minutes
                    : 60;
            })
            .Validate(static settings => !string.IsNullOrWhiteSpace(settings.Issuer),
                "Jwt:Issuer is not configured.")
            .Validate(static settings => !string.IsNullOrWhiteSpace(settings.Audience),
                "Jwt:Audience is not configured.")
            .Validate(static settings => settings.SigningKey.Length >= 32,
                "Jwt:SigningKey must contain at least 32 characters.")
            .Validate(
                static settings => !settings.SigningKey.StartsWith(
                    "REPLACE_",
                    StringComparison.OrdinalIgnoreCase),
                "Jwt:SigningKey cannot use the placeholder value.")
            .Validate(static settings => settings.AccessTokenMinutes is >= 5 and <= 1440,
                "Jwt:AccessTokenMinutes must be between 5 and 1440.")
            .ValidateOnStart();

        services.AddOptions<AccountSecuritySettings>()
            .Configure<IConfiguration>(static (settings, configuration) =>
            {
                settings.FailedLoginThreshold = int.TryParse(
                    configuration["AccountSecurity:FailedLoginThreshold"], out var threshold)
                    ? threshold : settings.FailedLoginThreshold;
                settings.LockoutMinutes = int.TryParse(
                    configuration["AccountSecurity:LockoutMinutes"], out var lockoutMinutes)
                    ? lockoutMinutes : settings.LockoutMinutes;
                settings.RefreshTokenDays = int.TryParse(
                    configuration["AccountSecurity:RefreshTokenDays"], out var refreshDays)
                    ? refreshDays : settings.RefreshTokenDays;
                settings.PasswordResetMinutes = int.TryParse(
                    configuration["AccountSecurity:PasswordResetMinutes"], out var resetMinutes)
                    ? resetMinutes : settings.PasswordResetMinutes;
            })
            .Validate(static settings => settings.FailedLoginThreshold is >= 3 and <= 20,
                "AccountSecurity:FailedLoginThreshold must be between 3 and 20.")
            .Validate(static settings => settings.LockoutMinutes is >= 1 and <= 1440,
                "AccountSecurity:LockoutMinutes must be between 1 and 1440.")
            .Validate(static settings => settings.RefreshTokenDays is >= 1 and <= 90,
                "AccountSecurity:RefreshTokenDays must be between 1 and 90.")
            .Validate(static settings => settings.PasswordResetMinutes is >= 5 and <= 1440,
                "AccountSecurity:PasswordResetMinutes must be between 5 and 1440.")
            .ValidateOnStart();

        services.AddOptions<EmailSettings>()
            .Configure<IConfiguration>(static (settings, configuration) =>
            {
                settings.Host = configuration["Email:Host"] ?? settings.Host;
                settings.Port = int.TryParse(configuration["Email:Port"], out var port)
                    ? port : settings.Port;
                settings.EnableSsl = bool.TryParse(
                    configuration["Email:EnableSsl"], out var enableSsl)
                    ? enableSsl : settings.EnableSsl;
                settings.SenderAddress = configuration["Email:SenderAddress"] ?? settings.SenderAddress;
                settings.SenderName = configuration["Email:SenderName"] ?? settings.SenderName;
                settings.Username = configuration["Email:Username"] ?? settings.Username;
                settings.Password = configuration["Email:Password"] ?? settings.Password;
                settings.PasswordResetUrl =
                    configuration["Email:PasswordResetUrl"] ?? settings.PasswordResetUrl;
            })
            .Validate(static settings => settings.Port is >= 1 and <= 65535,
                "Email:Port must be a valid TCP port.")
            .Validate(
                static settings => Uri.TryCreate(
                    settings.PasswordResetUrl,
                    UriKind.Absolute,
                    out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                "Email:PasswordResetUrl must be an absolute HTTP or HTTPS URL.")
            .ValidateOnStart();

        services.AddDbContext<AipmsDbContext>((serviceProvider, options) =>
        {
            var settings = serviceProvider
                .GetRequiredService<IOptions<DatabaseSettings>>()
                .Value;

            options.UseSqlServer(settings.DefaultConnection);
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHashingService, PasswordHashingService>();
        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
        services.AddSingleton<IOpaqueTokenService, OpaqueTokenService>();
        services.AddSingleton<IAccountSecurityPolicy>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<AccountSecuritySettings>>().Value);
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IAccessTokenAccountValidator, AccessTokenAccountValidator>();
        services.AddScoped<IUserAccountRepository, AccountSecurityRepository>();
        services.AddScoped<IRolePermissionRepository, AccountSecurityRepository>();
        services.AddScoped<IAuditLogRepository, AccountSecurityRepository>();
        services.AddScoped<IProjectAccessService, ProjectAccessService>();
        services.AddScoped<IProjectExecutionGuard, ProjectExecutionGuard>();
        services.AddScoped<IAcademicStructureRepository, AcademicStructureRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IMilestoneRepository, MilestoneRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IAuditTrail, DatabaseAuditTrail>();
        services.AddScoped<IPasswordResetNotifier, SmtpPasswordResetNotifier>();

        return services;
    }
}
