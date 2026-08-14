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

        services.AddDbContext<AipmsDbContext>((serviceProvider, options) =>
        {
            var settings = serviceProvider
                .GetRequiredService<IOptions<DatabaseSettings>>()
                .Value;

            options.UseSqlServer(settings.DefaultConnection);
        });

        return services;
    }
}
