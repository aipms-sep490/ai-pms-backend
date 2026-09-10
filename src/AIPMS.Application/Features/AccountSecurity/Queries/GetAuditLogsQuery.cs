using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Services;
using MediatR;

namespace AIPMS.Application.Features.AccountSecurity.Queries;

public sealed record GetAuditLogsQuery(
    long? ActorUserId,
    string? Action,
    string? EntityType,
    string? Outcome,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<AuditRecordDto>>;

public sealed class GetAuditLogsQueryHandler(
    IAuditLogRepository repository,
    AccountSecurityAccessService accessService)
    : IRequestHandler<GetAuditLogsQuery, PagedResult<AuditRecordDto>>
{
    public async Task<PagedResult<AuditRecordDto>> Handle(
        GetAuditLogsQuery request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var result = await repository.GetAuditLogsAsync(
            request.ActorUserId,
            request.Action?.Trim(),
            request.EntityType?.Trim(),
            request.Outcome?.Trim().ToUpperInvariant(),
            request.FromUtc,
            request.ToUtc,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new PagedResult<AuditRecordDto>(
            result.Items.Select(static record => record.ToDto()).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}
