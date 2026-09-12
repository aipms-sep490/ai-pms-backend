using AIPMS.Application.Features.Notifications.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Notifications.Events;

public enum WorkflowNotificationKind
{
    TeamInvitationSent, TeamInvitationAccepted, TeamInvitationRejected, TeamInvitationCancelled,
    SupervisorRequestSent, SupervisorRequestAccepted, SupervisorRequestRejected, SupervisorRequestCancelled,
    FinalSubmissionLocked
}

// Published synchronously inside the source workflow's transaction, never from a controller.
public sealed record WorkflowNotificationEvent(WorkflowNotificationKind Kind, long SourceId,
    long ActorId, DateTime OccurredAt) : INotification;

public sealed class WorkflowNotificationEventHandler(IWorkflowNotificationWriter writer)
    : INotificationHandler<WorkflowNotificationEvent>
{
    public Task Handle(WorkflowNotificationEvent notification, CancellationToken ct) => writer.WriteAsync(notification, ct);
}
