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
    [InlineData("MEETING_START_REMINDER", "notification.meeting.startReminder")]
    [InlineData("MEETING_ACTION_ASSIGNED", "notification.meetingAction.assigned")]
    [InlineData("PROGRESS_REPORT_SUBMITTED", "notification.progressReport.submitted")]
    [InlineData("DELIVERABLE_SUBMITTED", "notification.deliverable.submitted")]
    [InlineData("TASK_DEPENDENCY_IMPACT", "notification.task.dependencyImpact")]
    [InlineData("STUDENT_RESULT_PUBLISHED", "notification.studentResult.published")]
    [InlineData("PROJECT_RESULT_PUBLISHED", "notification.projectResult.published")]
    [InlineData("PROJECT_APPROVED", "notification.project.approved")]
    [InlineData("PROJECT_REJECTED", "notification.project.rejected")]
    [InlineData("PROJECT_REVISION_REQUESTED", "notification.project.revisionRequested")]
    [InlineData("SUPERVISOR_FEEDBACK_ADDED", "notification.supervisorFeedback.added")]
    [InlineData("EVALUATION_FINALIZED", "notification.evaluation.finalized")]
    [InlineData("EVALUATOR_ASSIGNED", "notification.evaluator.assigned")]
    [InlineData("COLD_EVALUATOR_ASSIGNED", "notification.cold.evaluator.assigned")]
    [InlineData("FINAL_SUBMISSION_LOCKED", "notification.finalSubmission.locked")]
    [InlineData("APPEAL_REQUESTED", "notification.appeal.requested")]
    [InlineData("APPEAL_REVIEWER_ASSIGNED", "notification.appeal.reviewerAssigned")]
    [InlineData("APPEAL_REVIEWED", "notification.appeal.reviewed")]
    [InlineData("APPEAL_DECIDED", "notification.appeal.decided")]
    [InlineData("REVIEW_GATE_UPDATED", "notification.reviewGate.updated")]
    [InlineData("REVIEW_GATE_3_SCHEDULED", "notification.reviewGate3.scheduled")]
    [InlineData("REVIEW_GATE_3_SCHEDULE_CHANGED", "notification.reviewGate3.scheduleChanged")]
    [InlineData("DEFENSE_SCHEDULED", "notification.defense.scheduled")]
    [InlineData("DEFENSE_SCHEDULE_CHANGED", "notification.defense.scheduleChanged")]
    [InlineData("DEFENSE_CANCELLED", "notification.defense.cancelled")]
    [InlineData("PROJECT_RISK_ALERT", "notification.projectRisk.alert")]
    [InlineData("PROJECT_RISK_MEDIUM", "notification.projectRisk.medium")]
    [InlineData("PROJECT_RISK_HIGH", "notification.projectRisk.high")]
    [InlineData("PROJECT_RISK_CRITICAL", "notification.projectRisk.critical")]
    [InlineData("TASK_DEADLINE_REMINDER", "notification.task.deadlineReminder")]
    [InlineData("TASK_OVERDUE", "notification.task.overdue")]
    [InlineData("DELIVERABLE_DEADLINE_REMINDER", "notification.deliverable.deadlineReminder")]
    [InlineData("DELIVERABLE_OVERDUE", "notification.deliverable.overdue")]
    [InlineData("FINAL_SUBMISSION_DEADLINE_REMINDER", "notification.finalSubmission.deadlineReminder")]
    [InlineData("FINAL_SUBMISSION_OVERDUE", "notification.finalSubmission.overdue")]
    [InlineData("INDUSTRY_INVITATION", "notification.industry.invitation")]
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
            "Công việc sắp đến hạn (PRJ-01): Hoàn thiện kiến trúc",
            "Công việc \"Hoàn thiện kiến trúc\" trong đồ án PRJ-01 sắp đến hạn.",
            "TASK",
            456,
            new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc),
            false,
            null);

        var dto = item.ToDto();

        Assert.Equal(123, dto.Id);
        Assert.Equal("TASK_DEADLINE_REMINDER", dto.NotificationType);
        Assert.Equal("notification.task.deadlineReminder", dto.TitleKey);
        Assert.Equal("Công việc sắp đến hạn (PRJ-01): Hoàn thiện kiến trúc", dto.Title);
        Assert.NotNull(dto.Params);
        Assert.Equal("TASK", dto.Params["entityType"]);
        Assert.Equal(456L, dto.Params["entityId"]);
        Assert.Equal("Công việc \"Hoàn thiện kiến trúc\" trong đồ án PRJ-01 sắp đến hạn.", dto.Params["rawContent"]);
    }
}
