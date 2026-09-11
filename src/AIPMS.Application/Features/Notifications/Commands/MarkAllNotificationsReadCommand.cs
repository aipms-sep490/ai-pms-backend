using AIPMS.Application.Features.Notifications.Services;
using MediatR;

namespace AIPMS.Application.Features.Notifications.Commands;

public sealed record MarkAllNotificationsReadCommand : IRequest;

public sealed class MarkAllNotificationsReadCommandHandler(NotificationInboxService inbox)
    : IRequestHandler<MarkAllNotificationsReadCommand>
{
    public Task Handle(MarkAllNotificationsReadCommand request, CancellationToken ct) => inbox.MarkAllReadAsync(ct);
}
