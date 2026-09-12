using System.Threading.Tasks;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class WorkflowNotificationWriter(AipmsDbContext context) : IWorkflowNotificationWriter
{
    public async Task WriteAsync(WorkflowNotificationEvent notification, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Workflow notifications require the source transaction.");
        var (entityType, status, type, title) = Describe(notification.Kind);
        long teamId;
        long? targetUser = null;
        string role;
        long? projectId = null;
        var finalSubmission = entityType == "FINAL_SUBMISSION";
        var projectResult = entityType == "PROJECT_RESULT";
        var departmentEvent = finalSubmission || entityType == "EVALUATION";

        // A source-row lock serializes duplicate event handling; inbox and transition commit together.
        if (projectResult)
        {
            var source = await context.Set<ProjectResult>().FromSqlInterpolated(
                $"SELECT * FROM dbo.project_results WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null) return;
            projectId = source.ProjectId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            role = AppRoles.Student;
        }
        else if (finalSubmission)
        {
            var source = await context.Set<FinalSubmission>().FromSqlInterpolated(
                $"SELECT * FROM dbo.final_submissions WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null) return;
            projectId = source.ProjectId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            role = AppRoles.DepartmentStaff;
        }
        else if (entityType == "EVALUATION")
        {
            var source = await context.Evaluations.FromSqlInterpolated(
                $"SELECT * FROM dbo.evaluations WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null || source.Status != "FINALIZED"
                || !await context.Set<EvaluationFinalization>().AnyAsync(f => f.EvaluationId == source.Id, ct)) return;
            projectId = source.ProjectId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            role = AppRoles.DepartmentStaff;
        }
        else if (entityType == "TEAM_INVITATION")
        {
            var source = await context.TeamInvitations.FromSqlInterpolated(
                $"SELECT * FROM dbo.team_invitations WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null || source.Status != status) return;
            teamId = source.TeamId;
            if (status is "PENDING" or "CANCELLED") targetUser = source.InvitedUserId;
            role = AppRoles.Student;
        }
        else
        {
            var source = await context.SupervisorRequests.FromSqlInterpolated(
                $"SELECT * FROM dbo.supervisor_requests WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null || source.Status != status) return;
            projectId = source.ProjectId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            if (status is "PENDING" or "CANCELLED")
                targetUser = await context.SupervisorProfiles.Where(p => p.Id == source.SupervisorProfileId)
                    .Select(p => p.UserId).SingleAsync(ct);
            role = targetUser.HasValue ? AppRoles.Lecturer : AppRoles.Student;
        }

        // Final-package routes are project-scoped, so the inbox carries the navigable project ID.
        var relatedEntityType = finalSubmission || projectResult ? "PROJECT" : entityType;
        var relatedEntityId = finalSubmission || projectResult ? projectId!.Value : notification.SourceId;
        if (await context.Notifications.AnyAsync(n => n.RelatedEntityType == relatedEntityType
            && n.RelatedEntityId == relatedEntityId && n.NotificationType == type, ct)) return;

        var organizationId = await context.Teams.Where(t => t.Id == teamId)
            .Select(t => t.AcademicSemester.OrganizationId).SingleAsync(ct);
        var recipients = context.Users.Where(u => u.Id != notification.ActorId && u.Status == "ACTIVE"
            && u.UserRoleUsers.Any(r => r.Role.Code == role)
            && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
            && u.Department.OrganizationId == organizationId);
        if (targetUser.HasValue)
            recipients = recipients.Where(u => u.Id == targetUser.Value);
        else if (projectResult)
            recipients = recipients.Where(u => u.TeamMembers.Any(m => m.TeamId == teamId && m.LeftAt == null));
        else if (!departmentEvent)
            recipients = recipients.Where(u => u.TeamMembers.Any(m => m.TeamId == teamId && m.IsLeader && m.LeftAt == null));
        if (departmentEvent)
            recipients = recipients.Where(u => context.ProjectMajors.Any(m => m.ProjectId == projectId
                && m.Major.IsActive && m.Major.DepartmentId == u.DepartmentId));
        if (entityType == "EVALUATION")
            recipients = recipients.Where(u => context.Set<EvaluationDraftState>().Any(s => s.EvaluationId == notification.SourceId
                && context.Set<EvaluationAssignment>().Any(a => a.Id == s.AssignmentId && a.DepartmentId == u.DepartmentId)));
        if (projectId.HasValue && role == AppRoles.Lecturer)
            recipients = recipients.Where(u => context.ProjectMajors.Any(m => m.ProjectId == projectId.Value
                && m.Major.IsActive && m.Major.DepartmentId == u.DepartmentId));
        var ids = await recipients.Select(u => u.Id).Distinct().ToListAsync(ct);
        if (ids.Count == 0) return;

        // No user-supplied messages or project details are copied into the inbox.
        context.Notifications.Add(new M.Notification
        {
            CreatedBy = notification.ActorId, NotificationType = type, Title = title, Content = title + ".",
            RelatedEntityType = relatedEntityType, RelatedEntityId = relatedEntityId,
            CreatedAt = notification.OccurredAt, UpdatedAt = notification.OccurredAt,
            NotificationRecipients = ids.Select(id => new M.NotificationRecipient
            {
                UserId = id, CreatedAt = notification.OccurredAt, UpdatedAt = notification.OccurredAt,
                DeliveredAt = notification.OccurredAt
            }).ToArray()
        });
        await context.SaveChangesAsync(ct);
    }

    private static (string Entity, string Status, string Type, string Title) Describe(WorkflowNotificationKind kind) => kind switch
    {
        WorkflowNotificationKind.ProjectResultPublished => ("PROJECT_RESULT", "PUBLISHED", "PROJECT_RESULT_PUBLISHED", "Your project's final result has been published"),
        WorkflowNotificationKind.EvaluationFinalized => ("EVALUATION", "FINALIZED", "EVALUATION_FINALIZED", "An evaluator finalized a project evaluation"),
        WorkflowNotificationKind.FinalSubmissionLocked => ("FINAL_SUBMISSION", "LOCKED", "FINAL_SUBMISSION_LOCKED", "A project submitted its final package"),
        WorkflowNotificationKind.TeamInvitationSent => ("TEAM_INVITATION", "PENDING", "TEAM_INVITATION_SENT", "You received a team invitation"),
        WorkflowNotificationKind.TeamInvitationAccepted => ("TEAM_INVITATION", "ACCEPTED", "TEAM_INVITATION_ACCEPTED", "A student accepted your team's invitation"),
        WorkflowNotificationKind.TeamInvitationRejected => ("TEAM_INVITATION", "REJECTED", "TEAM_INVITATION_REJECTED", "A student declined your team's invitation"),
        WorkflowNotificationKind.TeamInvitationCancelled => ("TEAM_INVITATION", "CANCELLED", "TEAM_INVITATION_CANCELLED", "A team invitation was cancelled"),
        WorkflowNotificationKind.SupervisorRequestSent => ("SUPERVISOR_REQUEST", "PENDING", "SUPERVISOR_REQUEST_SENT", "You received a supervision request"),
        WorkflowNotificationKind.SupervisorRequestAccepted => ("SUPERVISOR_REQUEST", "ACCEPTED", "SUPERVISOR_REQUEST_ACCEPTED", "Your project's supervision request was accepted"),
        WorkflowNotificationKind.SupervisorRequestRejected => ("SUPERVISOR_REQUEST", "REJECTED", "SUPERVISOR_REQUEST_REJECTED", "Your project's supervision request was declined"),
        WorkflowNotificationKind.SupervisorRequestCancelled => ("SUPERVISOR_REQUEST", "CANCELLED", "SUPERVISOR_REQUEST_CANCELLED", "A supervision request was cancelled"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
