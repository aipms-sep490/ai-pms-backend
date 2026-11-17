using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AccountSecurity.Models;

namespace AIPMS.Application.Features.AccountSecurity.Abstractions;

public interface IAuditLogRepository
{
    Task<bool> IsActiveAdministratorAsync(long userId, CancellationToken cancellationToken = default);

    Task<PagedResult<AccessEvent>> GetAccessEventsAsync(
        string? search,
        long? userId,
        string? eventType,
        string? outcome,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AuditRecord>> GetAuditLogsAsync(
        long? actorUserId,
        string? action,
        string? entityType,
        string? outcome,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
