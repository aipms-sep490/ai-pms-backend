using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Application.Features.Notifications.Services;
using MediatR;

namespace AIPMS.Application.Features.Notifications.Queries;

public sealed record GetUnreadNotificationCountQuery : IRequest<UnreadNotificationCountDto>;

public sealed class GetUnreadNotificationCountQueryHandler(NotificationInboxService inbox)
    : IRequestHandler<GetUnreadNotificationCountQuery, UnreadNotificationCountDto>
{
    public Task<UnreadNotificationCountDto> Handle(GetUnreadNotificationCountQuery request, CancellationToken ct) =>
        inbox.CountUnreadAsync(ct);
}
