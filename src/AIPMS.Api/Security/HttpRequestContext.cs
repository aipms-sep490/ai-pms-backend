using AIPMS.Application.Abstractions.Security;

namespace AIPMS.Api.Security;

internal sealed class HttpRequestContext(IHttpContextAccessor httpContextAccessor)
    : IRequestContext
{
    private const string CorrelationItemKey = "AIPMS.CorrelationId";

    private HttpContext? HttpContext => httpContextAccessor.HttpContext;

    public string? IpAddress => HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value[..Math.Min(value.Length, 500)];
        }
    }

    public Guid CorrelationId
    {
        get
        {
            var context = HttpContext;
            if (context is null)
            {
                return Guid.NewGuid();
            }

            if (context.Items.TryGetValue(CorrelationItemKey, out var existing)
                && existing is Guid correlationId)
            {
                return correlationId;
            }

            var supplied = context.Request.Headers["X-Correlation-ID"].ToString();
            correlationId = Guid.TryParse(supplied, out var parsed) ? parsed : Guid.NewGuid();
            context.Items[CorrelationItemKey] = correlationId;
            context.Response.Headers["X-Correlation-ID"] = correlationId.ToString();
            return correlationId;
        }
    }
}
