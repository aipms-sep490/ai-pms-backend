using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Infrastructure.Storage;
using AIPMS.Infrastructure.Identity;
using AIPMS.Infrastructure.Identity.Configuration;
using AIPMS.Infrastructure.Persistence.Configuration;
using AIPMS.Infrastructure.Persistence.Generated;
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
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IProjectAccessService, ProjectAccessService>();
        services.AddScoped<ISupervisorRepository, Persistence.Repositories.SupervisorRepository>();
        services.AddScoped<ISupervisorRequestRepository, Persistence.Repositories.SupervisorRequestRepository>();
        services.AddScoped<ISupervisorAssignmentRepository, Persistence.Repositories.SupervisorAssignmentRepository>();
        services.AddScoped<IUnitOfWork, Persistence.Repositories.UnitOfWork>();
        services.AddScoped<IDeliverableRepository, Persistence.Repositories.DeliverableRepository>();
        services.AddSingleton<IFileStorage, PrivateLocalFileStorage>();

        return services;
    }
}
