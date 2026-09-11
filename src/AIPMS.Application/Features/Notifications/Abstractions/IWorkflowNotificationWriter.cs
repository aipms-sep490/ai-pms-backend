using AIPMS.Application.Features.Notifications.Events;

namespace AIPMS.Application.Features.Notifications.Abstractions;

public interface IWorkflowNotificationWriter
{
    Task WriteAsync(WorkflowNotificationEvent notification, CancellationToken cancellationToken);
}
