using System.Globalization;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Services;

internal sealed class DatabaseAuditTrail(
    AipmsDbContext context,
    IRequestContext requestContext,
    TimeProvider timeProvider) : IAuditTrail
{
    public async Task RecordAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        context.AuditLogs.Add(new AuditLog
        {
            ActorUserId = entry.ActorUserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId?.ToString(CultureInfo.InvariantCulture),
            Outcome = entry.Outcome,
            CorrelationId = requestContext.CorrelationId,
            IpAddress = requestContext.IpAddress,
            UserAgent = requestContext.UserAgent,
            DetailsJson = JsonSerializer.Serialize(entry.Context),
            OccurredAt = timeProvider.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
