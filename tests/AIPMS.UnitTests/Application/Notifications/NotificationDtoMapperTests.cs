using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Application.Features.Notifications.Models;

namespace AIPMS.UnitTests.Application.Notifications;

public sealed class NotificationDtoMapperTests
{
    [Theory]
    [InlineData("TEAM_INVITATION_SENT", "notification.teamInvitation.sent")]
    [InlineData("TEAM_INVITATION_ACCEPTED", "notification.teamInvitation.accepted")]
    [InlineData("TEAM_INVITATION_REJECTED", "notification.teamInvitation.rejected")]
    [InlineData("TEAM_INVITATION_CANCELLED", "notification.teamInvitation.cancelled")]
    [InlineData("SUPERVISOR_REQUEST_SENT", "notification.supervisorRequest.sent")]
    [InlineData("SUPERVISOR_REQUEST_ACCEPTED", "notification.supervisorRequest.accepted")]
    [InlineData("SUPERVISOR_REQUEST_REJECTED", "notification.supervisorRequest.rejected")]
    [InlineData("SUPERVISOR_REQUEST_CANCELLED", "notification.supervisorRequest.cancelled")]
    [InlineData("SUPERVISOR_REPLACED", "notification.supervisor.replaced")]
    [InlineData("TEAM_LEADER_CHANGE_REQUESTED", "notification.teamLeaderChange.requested")]
    [InlineData("TEAM_LEADER_CHANGE_APPROVED", "notification.teamLeaderChange.approved")]
    [InlineData("TEAM_LEADER_CHANGE_REJECTED", "notification.teamLeaderChange.rejected")]
    [InlineData("MEETING_SCHEDULED", "notification.meeting.scheduled")]
    [InlineData("PROGRESS_REPORT_SUBMITTED", "notification.progressReport.submitted")]
    [InlineData("STUDENT_RESULT_PUBLISHED", "notification.studentResult.published")]
    [InlineData("PROJECT_RESULT_PUBLISHED", "notification.projectResult.published")]
    [InlineData("PROJECT_APPROVED", "notification.project.approved")]
    [InlineData("PROJECT_REJECTED", "notification.project.rejected")]
    [InlineData("PROJECT_REVISION_REQUESTED", "notification.project.revisionRequested")]
    [InlineData("SUPERVISOR_FEEDBACK_ADDED", "notification.supervisorFeedback.added")]
    [InlineData("EVALUATION_FINALIZED", "notification.evaluation.finalized")]
    [InlineData("EVALUATOR_ASSIGNED", "notification.evaluator.assigned")]
    [InlineData("FINAL_SUBMISSION_LOCKED", "notification.finalSubmission.locked")]
    [InlineData("TASK_DEADLINE_REMINDER", "notification.task.deadlineReminder")]
    [InlineData("DELIVERABLE_DEADLINE_REMINDER", "notification.deliverable.deadlineReminder")]
    [InlineData("DEADLINE_REMINDER", "notification.deadline.reminder")]
    public void ResolveTitleKey_MapsKnownTypesCorrectly(string type, string expectedKey)
    {
        var key = NotificationDtoMapper.ResolveTitleKey(type);
        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public void ToDto_PopulatesTitleKeyAndStructuredParams()
    {
        var item = new NotificationInboxItem(
            123,
            "TASK_DEADLINE_REMINDER",
            "A task is due soon",
            "Task 'Finalize Architecture' is due in 24 hours.",
            "TASK",
            456,
            new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc),
            false,
            null);

        var dto = item.ToDto();

        Assert.Equal(123, dto.Id);
        Assert.Equal("TASK_DEADLINE_REMINDER", dto.NotificationType);
        Assert.Equal("notification.task.deadlineReminder", dto.TitleKey);
        Assert.Equal("A task is due soon", dto.Title);
        Assert.NotNull(dto.Params);
        Assert.Equal("TASK", dto.Params["entityType"]);
        Assert.Equal(456L, dto.Params["entityId"]);
        Assert.Equal("Task 'Finalize Architecture' is due in 24 hours.", dto.Params["rawContent"]);
    }
}
