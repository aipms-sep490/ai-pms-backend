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
            "PROGRESS_REPORT_SUBMITTED" => "notification.progressReport.submitted",

            "STUDENT_RESULT_PUBLISHED" => "notification.studentResult.published",
            "PROJECT_RESULT_PUBLISHED" => "notification.projectResult.published",
            "PROJECT_APPROVED" => "notification.project.approved",
            "PROJECT_REJECTED" => "notification.project.rejected",
            "PROJECT_REVISION_REQUESTED" => "notification.project.revisionRequested",
            "SUPERVISOR_FEEDBACK_ADDED" => "notification.supervisorFeedback.added",

            "EVALUATION_FINALIZED" => "notification.evaluation.finalized",
            "EVALUATOR_ASSIGNED" => "notification.evaluator.assigned",
            "FINAL_SUBMISSION_LOCKED" => "notification.finalSubmission.locked",

            "APPEAL_SUBMITTED" => "notification.appeal.submitted",
            "APPEAL_DECIDED" => "notification.appeal.decided",
            "APPEAL_STATUS_CHANGED" => "notification.appeal.statusChanged",

            "REVIEW_GATE_DECISION" => "notification.reviewGate.decision",
            "INTERDISCIPLINARY_REVIEW_DECISION" => "notification.interdisciplinaryReview.decision",
            "DEFENSE_READINESS_CHANGED" => "notification.defenseReadiness.changed",

            "TASK_DEADLINE_REMINDER" => "notification.task.deadlineReminder",
            "DELIVERABLE_DEADLINE_REMINDER" => "notification.deliverable.deadlineReminder",
            "DEADLINE_REMINDER" => "notification.deadline.reminder",

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
