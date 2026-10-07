using AIPMS.Api.Configuration;
using AIPMS.Api.Middleware;
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
            await next();
        });
        app.UseSerilogRequestLogging();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors(CorsSettings.FrontendPolicyName);
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}
