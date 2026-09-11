using AIPMS.Application.Features.Notifications.Services;
using MediatR;

namespace AIPMS.Application.Features.Notifications.Commands;

public sealed record MarkNotificationReadCommand(long NotificationId) : IRequest;

public sealed class MarkNotificationReadCommandHandler(NotificationInboxService inbox)
    : IRequestHandler<MarkNotificationReadCommand>
{
    public Task Handle(MarkNotificationReadCommand request, CancellationToken ct) =>
        inbox.MarkReadAsync(request.NotificationId, ct);
}
