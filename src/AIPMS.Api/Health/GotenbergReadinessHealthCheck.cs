using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AIPMS.Api.Health;

public sealed class GotenbergReadinessHealthCheck(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public const string HttpClientName = "GotenbergHealthCheck";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var rawUrl = (configuration["OfficePreview:GotenbergUrl"] ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(rawUrl))
            return HealthCheckResult.Healthy("disabled");

        if (!Uri.TryCreate($"{rawUrl}/health", UriKind.Absolute, out var healthUri)
            || (healthUri.Scheme != Uri.UriSchemeHttp && healthUri.Scheme != Uri.UriSchemeHttps))
        {
            return FailureResult("invalid_url");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(healthUri, timeout.Token);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("ok")
                : FailureResult("unhealthy_status");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailureResult("timeout");
        }
        catch
        {
            return FailureResult("unreachable");
        }
    }

    private HealthCheckResult FailureResult(string reason)
    {
        var required = bool.TryParse(configuration["HealthChecks:RequireGotenberg"], out var r) && r;
        return required
            ? HealthCheckResult.Unhealthy(reason)
            : HealthCheckResult.Degraded(reason);
    }
}
