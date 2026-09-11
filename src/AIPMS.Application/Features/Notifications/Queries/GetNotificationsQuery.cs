using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Application.Features.Notifications.Services;
using MediatR;

namespace AIPMS.Application.Features.Notifications.Queries;

public sealed record GetNotificationsQuery(bool? IsRead = null, string? NotificationType = null,
    int Page = 1, int PageSize = 20) : IRequest<PagedResult<NotificationDto>>;

public sealed class GetNotificationsQueryHandler(NotificationInboxService inbox)
    : IRequestHandler<GetNotificationsQuery, PagedResult<NotificationDto>>
{
    public Task<PagedResult<NotificationDto>> Handle(GetNotificationsQuery request, CancellationToken ct) =>
        inbox.GetAsync(request.IsRead, request.NotificationType, request.Page, request.PageSize, ct);
}
