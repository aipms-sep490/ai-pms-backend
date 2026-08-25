namespace AIPMS.Application.Abstractions.Auditing;

public interface IAuditTrail
{
    Task RecordAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default);
}

public sealed record AuditEntry(
    long ActorUserId,
    string Action,
    string EntityType,
    long EntityId,
    IReadOnlyDictionary<string, object?> Context);
