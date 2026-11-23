using AIPMS.Application.Features.Notifications.Models;

namespace AIPMS.Application.Features.Notifications.DTOs;

internal static class NotificationDtoMapper
{
    public static NotificationDto ToDto(this NotificationInboxItem item) =>
        new(
            item.Id,
            item.NotificationType,
            item.Title,
            item.Content,
            item.RelatedEntityType,
            item.RelatedEntityId,
            DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc),
            item.IsRead,
            item.ReadAt.HasValue ? DateTime.SpecifyKind(item.ReadAt.Value, DateTimeKind.Utc) : null,
            ResolveTitleKey(item.NotificationType),
            ResolveParams(item));

    public static string ResolveTitleKey(string notificationType) =>
        notificationType switch
        {
            "TEAM_INVITATION_SENT" => "notification.teamInvitation.sent",
            "TEAM_INVITATION_ACCEPTED" => "notification.teamInvitation.accepted",
            "TEAM_INVITATION_REJECTED" => "notification.teamInvitation.rejected",
            "TEAM_INVITATION_CANCELLED" => "notification.teamInvitation.cancelled",

            "SUPERVISOR_REQUEST_SENT" => "notification.supervisorRequest.sent",
            "SUPERVISOR_REQUEST_ACCEPTED" => "notification.supervisorRequest.accepted",
            "SUPERVISOR_REQUEST_REJECTED" => "notification.supervisorRequest.rejected",
            "SUPERVISOR_REQUEST_CANCELLED" => "notification.supervisorRequest.cancelled",
            "SUPERVISOR_REPLACED" => "notification.supervisor.replaced",

            "TEAM_LEADER_CHANGE_REQUESTED" => "notification.teamLeaderChange.requested",
            "TEAM_LEADER_CHANGE_APPROVED" => "notification.teamLeaderChange.approved",
            "TEAM_LEADER_CHANGE_REJECTED" => "notification.teamLeaderChange.rejected",

            "MEETING_SCHEDULED" => "notification.meeting.scheduled",
            "MEETING_START_REMINDER" => "notification.meeting.startReminder",
            "MEETING_ACTION_ASSIGNED" => "notification.meetingAction.assigned",
            "PROGRESS_REPORT_SUBMITTED" => "notification.progressReport.submitted",
            "DELIVERABLE_SUBMITTED" => "notification.deliverable.submitted",
            "TASK_DEPENDENCY_IMPACT" => "notification.task.dependencyImpact",

            "STUDENT_RESULT_PUBLISHED" => "notification.studentResult.published",
            "PROJECT_RESULT_PUBLISHED" => "notification.projectResult.published",
            "PROJECT_APPROVED" => "notification.project.approved",
            "PROJECT_REJECTED" => "notification.project.rejected",
            "PROJECT_REVISION_REQUESTED" => "notification.project.revisionRequested",
            "SUPERVISOR_FEEDBACK_ADDED" => "notification.supervisorFeedback.added",

            "EVALUATION_FINALIZED" => "notification.evaluation.finalized",
            "EVALUATOR_ASSIGNED" => "notification.evaluator.assigned",
            "COLD_EVALUATOR_ASSIGNED" => "notification.cold.evaluator.assigned",
            "FINAL_SUBMISSION_LOCKED" => "notification.finalSubmission.locked",

            "APPEAL_REQUESTED" => "notification.appeal.requested",
            "APPEAL_REVIEWER_ASSIGNED" => "notification.appeal.reviewerAssigned",
            "APPEAL_REVIEWED" => "notification.appeal.reviewed",
            "APPEAL_DECIDED" => "notification.appeal.decided",

            "REVIEW_GATE_UPDATED" => "notification.reviewGate.updated",
            "REVIEW_GATE_3_SCHEDULED" => "notification.reviewGate3.scheduled",
            "REVIEW_GATE_3_SCHEDULE_CHANGED" => "notification.reviewGate3.scheduleChanged",
            "DEFENSE_SCHEDULED" => "notification.defense.scheduled",
            "DEFENSE_SCHEDULE_CHANGED" => "notification.defense.scheduleChanged",
            "DEFENSE_CANCELLED" => "notification.defense.cancelled",

            "PROJECT_RISK_ALERT" => "notification.projectRisk.alert",
            "PROJECT_RISK_MEDIUM" => "notification.projectRisk.medium",
            "PROJECT_RISK_HIGH" => "notification.projectRisk.high",
            "PROJECT_RISK_CRITICAL" => "notification.projectRisk.critical",

            "TASK_DEADLINE_REMINDER" => "notification.task.deadlineReminder",
            "TASK_OVERDUE" => "notification.task.overdue",
            "DELIVERABLE_DEADLINE_REMINDER" => "notification.deliverable.deadlineReminder",
            "DELIVERABLE_OVERDUE" => "notification.deliverable.overdue",
            "FINAL_SUBMISSION_DEADLINE_REMINDER" => "notification.finalSubmission.deadlineReminder",
            "FINAL_SUBMISSION_OVERDUE" => "notification.finalSubmission.overdue",

            "INDUSTRY_INVITATION" => "notification.industry.invitation",

            _ => $"notification.{ToCamelCase(notificationType)}"
        };

    private static IReadOnlyDictionary<string, object?> ResolveParams(NotificationInboxItem item)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(item.RelatedEntityType))
        {
            dict["entityType"] = item.RelatedEntityType;
        }
        if (item.RelatedEntityId.HasValue)
        {
            dict["entityId"] = item.RelatedEntityId.Value;
        }
        if (!string.IsNullOrWhiteSpace(item.Content))
        {
            dict["rawContent"] = item.Content;
        }
        return dict;
    }

    private static string ToCamelCase(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "generic";
        var parts = type.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "generic";
        var result = parts[0].ToLowerInvariant();
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                result += char.ToUpperInvariant(parts[i][0]) + parts[i][1..].ToLowerInvariant();
            }
        }
        return result;
    }
}
