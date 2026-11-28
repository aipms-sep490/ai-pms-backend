using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AIPMS.Api.Health;

public sealed class FileStorageReadinessHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = (configuration["FileStorage:Provider"] ?? "Local").Trim();
        if (string.Equals(provider, "GoogleDrive", StringComparison.OrdinalIgnoreCase))
        {
            var hasCredentials = !string.IsNullOrWhiteSpace(configuration["FileStorage:GoogleDrive:CredentialsPath"])
                || !string.IsNullOrWhiteSpace(configuration["FileStorage:GoogleDrive:CredentialsJson"])
                || !string.IsNullOrWhiteSpace(configuration["FileStorage:GoogleDrive:RefreshToken"]);
            var hasFolder = !string.IsNullOrWhiteSpace(configuration["FileStorage:GoogleDrive:RootFolderId"]);
            return hasCredentials && hasFolder
                ? HealthCheckResult.Healthy("configured")
                : HealthCheckResult.Unhealthy("misconfigured");
        }

        var rootPath = configuration["FileStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = Path.Combine(Path.GetTempPath(), "aipms-files");
        }

        try
        {
            var fullRoot = Path.GetFullPath(rootPath);
            Directory.CreateDirectory(fullRoot);
            var probePath = Path.Combine(fullRoot, $".healthcheck-{Guid.NewGuid():N}.tmp");
            await System.IO.File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            System.IO.File.Delete(probePath);
            return HealthCheckResult.Healthy("writable");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("unwritable");
        }
    }
}
