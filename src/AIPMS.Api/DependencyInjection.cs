using AIPMS.Api.Security;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;
using AIPMS.Api.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<IAuthorizationHandler, ProjectAccessAuthorizationHandler>();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole(AppRoles.Admin));
            options.AddPolicy(
                AuthorizationPolicies.AcademicManagement,
                policy => policy.RequireRole(AppRoles.Admin, AppRoles.DepartmentStaff));
            options.AddPolicy(
                AuthorizationPolicies.LecturerOnly,
                policy => policy.RequireRole(AppRoles.Lecturer));
            options.AddPolicy(
                AuthorizationPolicies.StudentOnly,
                policy => policy.RequireRole(AppRoles.Student));
            options.AddPolicy(
                AuthorizationPolicies.ProjectAccess,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(new ProjectAccessRequirement());
                });
        });

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

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter the JWT access token."
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                }] = Array.Empty<string>()
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
