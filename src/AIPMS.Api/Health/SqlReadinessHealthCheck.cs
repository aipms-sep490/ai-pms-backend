using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AIPMS.Api.Health;

public sealed class SqlReadinessHealthCheck(AipmsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
            return HealthCheckResult.Healthy("ok");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("timeout");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("unreachable");
        }
    }
}
