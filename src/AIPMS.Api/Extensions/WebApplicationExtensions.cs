using System.Text.Json;
using AIPMS.Api.Configuration;
using AIPMS.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

namespace AIPMS.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/hubs/chat"))
            {
                var chat = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AIPMS.Application.Features.Chat.ChatSettings>>().Value;
                if (!chat.Enabled || !chat.RealtimeEnabled) { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                var cors = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<CorsSettings>>().Value;
                var origin = context.Request.Headers.Origin.ToString();
                if (string.IsNullOrEmpty(origin) || !cors.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
            }
            else if (context.Request.Path.StartsWithSegments("/hubs/notifications"))
            {
                var config = context.RequestServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                if (config.GetValue<bool?>("NotificationRealtime:Enabled") == false)
                { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                var origin = context.Request.Headers.Origin.ToString();
                if (!string.IsNullOrEmpty(origin))
                {
                    var cors = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<CorsSettings>>().Value;
                    if (!cors.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                    { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
                }
            }
            await next();
        });
        app.UseSerilogRequestLogging();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseStatusCodePages();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors(CorsSettings.FrontendPolicyName);
        app.UseAuthentication();
        app.UseMiddleware<AuthRateLimitingKeyMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = WriteReadinessResponseAsync
        }).AllowAnonymous();
        app.MapControllers();

        return app;
    }

    private static Task WriteReadinessResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var checks = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, entry) in report.Entries)
        {
            checks[name] = new
            {
                status = entry.Status.ToString(),
                detail = entry.Description ?? entry.Status.ToString().ToLowerInvariant()
            };
        }

        var payload = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks
        });
        return context.Response.WriteAsync(payload);
    }
}
