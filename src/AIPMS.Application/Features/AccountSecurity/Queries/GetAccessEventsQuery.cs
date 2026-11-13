using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Services;
using MediatR;

namespace AIPMS.Application.Features.AccountSecurity.Queries;

public sealed record GetAccessEventsQuery(
    string? Search,
    long? UserId,
    string? EventType,
    string? Outcome,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<AccessEventDto>>;

public sealed class GetAccessEventsQueryHandler(
    IAuditLogRepository repository,
    AccountSecurityAccessService accessService)
    : IRequestHandler<GetAccessEventsQuery, PagedResult<AccessEventDto>>
{
    public async Task<PagedResult<AccessEventDto>> Handle(
        GetAccessEventsQuery request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var result = await repository.GetAccessEventsAsync(
            request.Search?.Trim(),
            request.UserId,
            request.EventType?.Trim().ToUpperInvariant(),
            request.Outcome?.Trim().ToUpperInvariant(),
            request.FromUtc,
            request.ToUtc,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new PagedResult<AccessEventDto>(
            result.Items.Select(static item => new AccessEventDto(
                item.Id, item.UserId, item.FullName, item.Email, item.EventType,
                item.AuthenticationMethod, item.Outcome, item.IpAddress, item.OccurredAt)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}
