using AIPMS.Api.Configuration;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Formatting.Compact;

namespace AIPMS.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddOptions<CorsSettings>()
            .BindConfiguration(CorsSettings.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                static settings => settings.AllowedOrigins.All(IsAbsoluteHttpOrigin),
                "Every CORS origin must be an absolute HTTP or HTTPS URL.")
            .ValidateOnStart();

        services.AddOptions<ObservabilitySettings>()
            .BindConfiguration(ObservabilitySettings.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                static settings => !string.IsNullOrWhiteSpace(settings.LogFilePath),
                "The observability log file path cannot be empty.")
            .ValidateOnStart();

        services.AddSerilog((serviceProvider, loggerConfiguration) =>
        {
            var settings = serviceProvider
                .GetRequiredService<IOptions<ObservabilitySettings>>()
                .Value;

            loggerConfiguration
                .MinimumLevel.Is(settings.MinimumLevel)
                .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", settings.ApplicationName)
                .WriteTo.Console()
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    settings.LogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: settings.RetainedFileCountLimit,
                    shared: true);
        });

        services.AddProblemDetails();
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "AI-PMS API",
                Version = "v1",
                Description = "Academic project lifecycle management and AI decision support."
            });
        });

        services.AddSingleton<IConfigureOptions<CorsOptions>, ConfigureCorsOptions>();
        services.AddCors();

        return services;
    }

    private static bool IsAbsoluteHttpOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
