using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AccountSecurity.Models;

namespace AIPMS.Application.Features.AccountSecurity.Abstractions;

public interface IAuditLogRepository
{
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
