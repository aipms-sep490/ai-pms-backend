using System.Text.Json;

namespace AIPMS.Api.Middleware;

public sealed class AuthRateLimitingKeyMiddleware(RequestDelegate next)
{
    public const string AuthEmailItemKey = "AuthEmailKey";

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("email", out var emailProp)
                        && emailProp.ValueKind == JsonValueKind.String)
                    {
                        var email = emailProp.GetString();
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            context.Items[AuthEmailItemKey] = email.Trim().ToLowerInvariant();
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed JSON; will be handled by MVC model binding
                }
            }
        }

        await next(context);
    }
}
