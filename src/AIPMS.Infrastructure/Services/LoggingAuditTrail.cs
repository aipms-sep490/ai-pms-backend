using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using Microsoft.Extensions.Logging;

namespace AIPMS.Infrastructure.Services;

internal sealed class LoggingAuditTrail(ILogger<LoggingAuditTrail> logger) : IAuditTrail
{
    public Task RecordAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Audit {AuditAction} on {EntityType} {EntityId} by user {ActorUserId} with context {@AuditContext}",
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.ActorUserId,
            entry.Context);

        return Task.CompletedTask;
    }
}
