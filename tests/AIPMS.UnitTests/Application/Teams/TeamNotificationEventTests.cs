using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Notifications.Events;

namespace AIPMS.UnitTests.Application.Teams;

public sealed partial class TeamHandlerTests
{
    [Fact]
    public async Task Invitation_transitions_publish_once_inside_transaction_with_source_identity()
    {
        var h = new Harness();
        var invitation = await h.Workflow.InviteAsync(new(1, 3, "Message"), default);
        var sent = Assert.IsType<WorkflowNotificationEvent>(Assert.Single(h.Events.Events));
        Assert.Equal(new(WorkflowNotificationKind.TeamInvitationSent, invitation.Id, 1, Now), sent);
        h.Actor.UserId = 3;
        await h.Workflow.AcceptAsync(invitation.Id, default);
        Assert.Equal(new WorkflowNotificationEvent(WorkflowNotificationKind.TeamInvitationAccepted, invitation.Id, 3, Now), h.Events.Events[1]);
        await Assert.ThrowsAsync<ConflictException>(() => h.Workflow.AcceptAsync(invitation.Id, default));
        Assert.Equal(2, h.Events.Events.Count);
    }

    [Fact]
    public async Task Unauthorized_response_and_duplicate_send_do_not_publish()
    {
        var h = new Harness();
        h.Invite(3);
        await Assert.ThrowsAsync<ConflictException>(() => h.Workflow.InviteAsync(new(1, 3, null), default));
        h.Actor.UserId = 2;
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Workflow.RejectAsync(1, default));
        Assert.Empty(h.Events.Events);
    }
}
