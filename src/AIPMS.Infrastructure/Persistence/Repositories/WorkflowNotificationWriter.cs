using System.Threading.Tasks;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using EmailDeliveryRow = AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class WorkflowNotificationWriter(AipmsDbContext context) : IWorkflowNotificationWriter
{
    public async Task WriteAsync(WorkflowNotificationEvent notification, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Workflow notifications require the source transaction.");
        var (entityType, status, type) = Describe(notification.Kind);
        long teamId;
        long? targetUser = null;
        string role;
        long? projectId = null;
        long? historyId = null;
        M.Meeting? meetingSource = null;
        var isColdEvaluation = false;
        var finalSubmission = entityType == "FINAL_SUBMISSION";
        var projectResult = entityType == "PROJECT_RESULT";
        var projectStateEvent = entityType is "PROJECT_APPROVED" or "PROJECT_REJECTED" or "PROJECT_REVISION";
        var projectSubmittedEvent = entityType == "PROJECT_SUBMITTED";
        var projectActivatedEvent = entityType == "PROJECT_ACTIVATED";
        var slotReleasedEvent = entityType == "SUPERVISOR_SLOT_RELEASED";
        var evaluatorRevokedEvent = entityType == "EVALUATION_ASSIGNMENT_REVOKED";
        long[] releasedLecturers = [];
        var feedbackEvent = entityType == "SUPERVISOR_FEEDBACK";
        var replacementEvent = entityType == "SUPERVISOR_ASSIGNMENT";
        long[] replacementLecturers = [];
        var leaderChangeEvent = entityType == "TEAM_LEADER_CHANGE_REQUEST";
        var meetingEvent = entityType == "MEETING";
        var reportEvent = entityType == "PROGRESS_REPORT";
        var departmentEvent = finalSubmission || entityType == "EVALUATION" || projectSubmittedEvent;

        // A source-row lock serializes duplicate event handling; inbox and transition commit together.
        if (entityType == "STUDENT_RESULT")
        {
            var source = await context.Set<StudentResult>().FromSqlInterpolated(
                $"SELECT * FROM dbo.student_results WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null) return;
            projectId = source.ProjectId; targetUser = source.StudentId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            role = AppRoles.Student;
        }
        else if (meetingEvent)
        {
            var source = await context.Meetings.FromSqlInterpolated(
                $"SELECT * FROM dbo.meetings WITH (UPDLOCK,HOLDLOCK) WHERE id={notification.SourceId}").AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null || source.Status != status || source.CreatedBy != notification.ActorId) return;
            projectId = source.ProjectId;
            meetingSource = source;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            role = AppRoles.Student;
        }
        else if (reportEvent)
        {
            var source = await context.ProgressReports.FromSqlInterpolated(
                $"SELECT * FROM dbo.progress_reports WITH (UPDLOCK,HOLDLOCK) WHERE id={notification.SourceId}").AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null || source.Status != status) return;
            projectId = source.ProjectId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            role = AppRoles.Lecturer;
        }
        else if (replacementEvent)
        {
            var source = await context.SupervisorAssignments.AsNoTracking()
                .Where(a => a.Id == notification.SourceId && a.ReplacesAssignmentId != null)
                .Select(a => new { a.ProjectId, a.SupervisorProfile.UserId, a.ReplacesAssignmentId }).SingleOrDefaultAsync(ct);
            if (source is null) return;
            var previousUser = await context.SupervisorAssignments.Where(a => a.Id == source.ReplacesAssignmentId)
                .Select(a => a.SupervisorProfile.UserId).SingleAsync(ct);
            replacementLecturers = [source.UserId, previousUser];
            projectId = source.ProjectId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            role = AppRoles.Student;
        }
        else if (entityType == "EVALUATION_ASSIGNMENT")
        {
            var source = await context.Set<EvaluationAssignment>().FromSqlInterpolated(
                $"SELECT * FROM dbo.evaluation_assignments WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null || source.Status != "ACTIVE" || source.AssignedBy != notification.ActorId) return;
            if (source.Stage == "COLD")
            {
                type = "COLD_EVALUATOR_ASSIGNED";
                isColdEvaluation = true;
            }
            projectId = source.ProjectId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            targetUser = source.EvaluatorId;
            role = AppRoles.Lecturer;
        }
        else if (evaluatorRevokedEvent)
        {
            var source = await context.Set<EvaluationAssignment>().FromSqlInterpolated(
                $"SELECT * FROM dbo.evaluation_assignments WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null || source.Status != "REVOKED") return;
            projectId = source.ProjectId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            targetUser = source.EvaluatorId;
            role = AppRoles.Lecturer;
        }
        else if (projectSubmittedEvent)
        {
            var project = await context.Projects.FromSqlInterpolated(
                $"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (project is null || project.Status is not ("SUBMITTED" or "UNDER_REVIEW")) return;
            projectId = project.Id;
            teamId = project.TeamId;
            role = AppRoles.DepartmentStaff;
        }
        else if (projectActivatedEvent)
        {
            var project = await context.Projects.FromSqlInterpolated(
                $"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (project is null || project.Status != "ACTIVE") return;
            projectId = project.Id;
            teamId = project.TeamId;
            role = AppRoles.Student;
        }
        else if (slotReleasedEvent)
        {
            var project = await context.Projects.FromSqlInterpolated(
                $"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (project is null || project.Status != "REJECTED") return;
            if (notification.SourceVersion != Convert.ToBase64String(project.RowVersion)) return;
            historyId = await context.ProjectStatusHistories.Where(h => h.ProjectId == project.Id
                    && h.NewStatus == "REJECTED" && h.ChangedBy == notification.ActorId)
                .OrderByDescending(h => h.Id).Select(h => (long?)h.Id).FirstOrDefaultAsync(ct);
            if (!historyId.HasValue) return;
            var releasedReqUsers = await context.SupervisorRequests.AsNoTracking()
                .Where(r => r.ProjectId == project.Id && r.Status == "RELEASED" && r.UpdatedAt == project.UpdatedAt)
                .Select(r => r.SupervisorProfile.UserId)
                .ToListAsync(ct);
            var endedAssignUsers = await context.SupervisorAssignments.AsNoTracking()
                .Where(a => a.ProjectId == project.Id && a.EndedAt == project.UpdatedAt)
                .Select(a => a.SupervisorProfile.UserId)
                .ToListAsync(ct);
            releasedLecturers = releasedReqUsers.Concat(endedAssignUsers).Distinct().ToArray();
            if (releasedLecturers.Length == 0) return;
            projectId = project.Id;
            teamId = project.TeamId;
            role = AppRoles.Lecturer;
        }
        else if (projectStateEvent)
        {
            var project = await context.Projects.FromSqlInterpolated(
                $"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (project is null || project.Status != status) return;
            if (notification.Kind is WorkflowNotificationKind.ProjectRejected or WorkflowNotificationKind.ProjectRevisionRequested)
            {
                if (notification.SourceVersion != Convert.ToBase64String(project.RowVersion)) return;
                historyId = await context.ProjectStatusHistories.Where(h => h.ProjectId == project.Id
                        && h.NewStatus == status && h.ChangedBy == notification.ActorId)
                    .OrderByDescending(h => h.Id).Select(h => (long?)h.Id).FirstOrDefaultAsync(ct);
                if (!historyId.HasValue) return;
            }
            projectId = project.Id;
            teamId = project.TeamId;
            role = AppRoles.Student;
        }
        else if (projectResult)
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
        else if (feedbackEvent)
        {
            var source = await context.SupervisorFeedbacks.FromSqlInterpolated(
                $"SELECT * FROM dbo.supervisor_feedback WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null) return;
            // Resolve recipients only for a valid, auditable parent and its assigned supervisor.
            if (new[] { source.ProgressReportId, source.DeliverableVersionId, source.MeetingId }.Count(id => id.HasValue) != 1
                || !await context.SupervisorAssignments.AnyAsync(a => a.Id == source.SupervisorAssignmentId
                    && a.ProjectId == source.ProjectId && a.EndedAt == null
                    && a.SupervisorProfile.UserId == notification.ActorId, ct)) return;
            var validParent = source.ProgressReportId.HasValue
                ? await context.ProgressReports.AnyAsync(p => p.Id == source.ProgressReportId && p.ProjectId == source.ProjectId && p.Status != "DRAFT", ct)
                : source.DeliverableVersionId.HasValue
                    ? await context.DeliverableVersions.AnyAsync(v => v.Id == source.DeliverableVersionId && v.Deliverable.ProjectId == source.ProjectId, ct)
                    : await context.Meetings.AnyAsync(m => m.Id == source.MeetingId && m.ProjectId == source.ProjectId && m.Status != "CANCELLED", ct);
            if (!validParent) return;
            projectId = source.ProjectId;
            teamId = await context.Projects.Where(p => p.Id == source.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            role = AppRoles.Student;
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
        else if (leaderChangeEvent)
        {
            var source = await context.Set<M.TeamLeaderChangeRequest>().FromSqlInterpolated(
                $"SELECT * FROM dbo.team_leader_change_requests WITH (UPDLOCK, HOLDLOCK) WHERE id = {notification.SourceId}")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source is null || source.Status != status) return;
            projectId = source.ProjectId;
            teamId = source.TeamId;
            if (status == "PENDING")
            {
                if (source.MentorProfileId.HasValue)
                {
                    targetUser = await context.SupervisorProfiles.Where(p => p.Id == source.MentorProfileId.Value)
                        .Select(p => (long?)p.UserId).SingleOrDefaultAsync(ct);
                    role = AppRoles.Lecturer;
                }
                else
                {
                    var projScope = await AIPMS.Infrastructure.Services.Projects.ProjectAcademicScopeReader.ReadAsync(context, source.ProjectId, ct);
                    if (projScope.LeadDepartmentId.HasValue)
                    {
                        replacementLecturers = await AIPMS.Infrastructure.Services.Projects.V4WorkflowNotificationHelper
                            .GetDepartmentStaffIdsAsync(context, projScope.LeadDepartmentId.Value, ct);
                    }
                    role = AppRoles.DepartmentStaff;
                }
            }
            else if (status == "APPROVED")
            {
                // BN-09: notify both previous leader/requester and newly appointed leader.
                replacementLecturers = new[] { source.RequestedBy, source.CurrentLeaderUserId, source.NewLeaderUserId }.Distinct().ToArray();
                role = AppRoles.Student;
            }
            else
            {
                targetUser = source.RequestedBy;
                role = AppRoles.Student;
            }
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

        // Store occurrence identity for deduplication; the inbox maps history/feedback to navigable parents.
        var projectScoped = finalSubmission || projectResult || projectStateEvent || projectSubmittedEvent || projectActivatedEvent;
        var relatedEntityType = historyId.HasValue ? "PROJECT_STATUS_HISTORY"
            : evaluatorRevokedEvent ? "EVALUATION_ASSIGNMENT"
            : projectScoped ? "PROJECT"
            : entityType;
        var relatedEntityId = historyId ?? (projectScoped ? projectId!.Value : notification.SourceId);
        if (await context.Notifications.AnyAsync(n => n.RelatedEntityType == relatedEntityType
            && n.RelatedEntityId == relatedEntityId && n.NotificationType == type, ct)) return;

        var teamScope = await context.Teams.Where(t => t.Id == teamId)
            .Select(t => new { t.AcademicSemester.OrganizationId, t.Name }).SingleAsync(ct);
        var organizationId = teamScope.OrganizationId;
        List<long> ids;
        List<long> inAppOnlyIds = [];
        if (meetingEvent)
        {
            var participantUserIds = await context.MeetingParticipants
                .Where(mp => mp.MeetingId == notification.SourceId)
                .Select(mp => mp.UserId)
                .ToListAsync(ct);

            ids = await context.Users
                .Where(u => u.Id != notification.ActorId && u.Status == "ACTIVE" && participantUserIds.Contains(u.Id))
                .Select(u => u.Id)
                .Distinct()
                .ToListAsync(ct);
        }
        else if (reportEvent)
        {
            var supervisorUserIds = await context.SupervisorAssignments
                .Where(a => a.ProjectId == projectId && a.EndedAt == null && a.IsPrimary && a.AssignmentType == "PRIMARY")
                .Select(a => a.SupervisorProfile.UserId)
                .ToListAsync(ct);

            ids = await context.Users
                .Where(u => u.Id != notification.ActorId && u.Status == "ACTIVE" && supervisorUserIds.Contains(u.Id)
                    && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer)
                    && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
                    && u.Department.OrganizationId == organizationId)
                .Select(u => u.Id)
                .Distinct()
                .ToListAsync(ct);
        }
        else if (projectActivatedEvent)
        {
            var studentUserIds = await context.TeamMembers
                .Where(m => m.TeamId == teamId && m.LeftAt == null)
                .Select(m => m.UserId)
                .ToListAsync(ct);
            var supUserIds = await context.SupervisorAssignments
                .Where(a => a.ProjectId == projectId && a.EndedAt == null)
                .Select(a => a.SupervisorProfile.UserId)
                .ToListAsync(ct);
            var combined = studentUserIds.Concat(supUserIds).Where(id => id != notification.ActorId).Distinct().ToArray();
            ids = combined.Length == 0
                ? []
                : await context.Users
                    .Where(u => combined.Contains(u.Id) && u.Status == "ACTIVE"
                        && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
                        && u.Department.OrganizationId == organizationId)
                    .Select(u => u.Id)
                    .Distinct()
                    .ToListAsync(ct);
        }
        else if (slotReleasedEvent)
        {
            ids = await context.Users
                .Where(u => u.Id != notification.ActorId && u.Status == "ACTIVE"
                    && releasedLecturers.Contains(u.Id)
                    && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer)
                    && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
                    && u.Department.OrganizationId == organizationId)
                .Select(u => u.Id)
                .Distinct()
                .ToListAsync(ct);
        }
        else
        {
            var recipients = context.Users.Where(u => (u.Id != notification.ActorId || entityType == "EVALUATION_ASSIGNMENT" || evaluatorRevokedEvent) && u.Status == "ACTIVE"
                && (u.UserRoleUsers.Any(r => r.Role.Code == role)
                    || (replacementEvent && replacementLecturers.Contains(u.Id) && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer)))
                && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
                && u.Department.OrganizationId == organizationId);
            if (replacementEvent)
                recipients = recipients.Where(u => replacementLecturers.Contains(u.Id)
                    || u.TeamMembers.Any(m => m.TeamId == teamId && m.LeftAt == null));
            else if (leaderChangeEvent && replacementLecturers.Length > 0)
                recipients = recipients.Where(u => replacementLecturers.Contains(u.Id));
            else if (targetUser.HasValue)
                recipients = recipients.Where(u => u.Id == targetUser.Value);
            else if (projectResult || projectStateEvent || feedbackEvent)
                recipients = recipients.Where(u => u.TeamMembers.Any(m => m.TeamId == teamId && m.LeftAt == null));
            else if (!departmentEvent)
                recipients = recipients.Where(u => u.TeamMembers.Any(m => m.TeamId == teamId && m.IsLeader && m.LeftAt == null));
            if (departmentEvent)
                recipients = recipients.Where(u => context.ProjectMajors.Any(m => m.ProjectId == projectId
                    && m.Major.IsActive && m.Major.DepartmentId == u.DepartmentId));
            if (entityType == "EVALUATION")
                recipients = recipients.Where(u => context.Set<EvaluationDraftState>().Any(s => s.EvaluationId == notification.SourceId
                    && context.Set<EvaluationAssignment>().Any(a => a.Id == s.AssignmentId && a.DepartmentId == u.DepartmentId)));
            if (projectId.HasValue && role == AppRoles.Lecturer && !leaderChangeEvent)
                recipients = recipients.Where(u => context.ProjectMajors.Any(m => m.ProjectId == projectId.Value
                    && m.Major.IsActive && m.Major.DepartmentId == u.DepartmentId));
            ids = await recipients.Select(u => u.Id).Distinct().ToListAsync(ct);

            if ((finalSubmission || projectResult) && projectId.HasValue)
            {
                var primarySupIds = await context.SupervisorAssignments
                    .Where(a => a.ProjectId == projectId.Value && a.EndedAt == null && a.IsPrimary && a.AssignmentType == "PRIMARY"
                        && a.SupervisorProfile.UserId != notification.ActorId
                        && a.SupervisorProfile.User.Status == "ACTIVE")
                    .Select(a => a.SupervisorProfile.UserId)
                    .Distinct()
                    .ToListAsync(ct);
                foreach (var supId in primarySupIds)
                {
                    if (!ids.Contains(supId))
                    {
                        ids.Add(supId);
                        if (projectResult) inAppOnlyIds.Add(supId);
                    }
                }
            }
        }
        var emailRecipientIds = inAppOnlyIds.Count == 0 ? ids : ids.Where(id => !inAppOnlyIds.Contains(id)).ToList();
        if (ids.Count == 0) return;

        var projectInfo = projectId.HasValue
            ? await context.Projects.AsNoTracking().Where(p => p.Id == projectId.Value)
                .Select(p => new { p.Code, p.Title }).SingleOrDefaultAsync(ct)
            : null;
        var actorName = await context.Users.AsNoTracking().Where(u => u.Id == notification.ActorId)
            .Select(u => u.FullName).SingleOrDefaultAsync(ct);
        var (title, content) = BuildMessage(notification.Kind, isColdEvaluation, projectInfo?.Code, projectInfo?.Title, teamScope.Name, meetingSource, actorName);

        // No user-supplied invitation/request messages are copied into the inbox.
        var inbox = new M.Notification
        {
            CreatedBy = notification.ActorId, NotificationType = type, Title = Truncate(title), Content = content,
            RelatedEntityType = relatedEntityType, RelatedEntityId = relatedEntityId,
            CreatedAt = notification.OccurredAt, UpdatedAt = notification.OccurredAt,
            NotificationRecipients = ids.Select(id => new M.NotificationRecipient
            {
                UserId = id, CreatedAt = notification.OccurredAt, UpdatedAt = notification.OccurredAt,
                DeliveredAt = notification.OccurredAt
            }).ToArray()
        };
        context.Notifications.Add(inbox);
        await context.SaveChangesAsync(ct);
        // Proposal submission notices stay in-app; projectResult notifies Primary Supervisor in-app only; all other critical transitions enqueue SMTP delivery.
        if (projectSubmittedEvent || emailRecipientIds.Count == 0) return;
        context.Set<EmailDeliveryRow>().AddRange(emailRecipientIds.Select(id => new EmailDeliveryRow
        {
            NotificationRecipientId = context.NotificationRecipients.Local
                .Single(r => r.NotificationId == inbox.Id && r.UserId == id).Id,
            NextAttemptAt = notification.OccurredAt
        }));
        await context.SaveChangesAsync(ct);
    }

    private static (string Entity, string Status, string Type) Describe(WorkflowNotificationKind kind) => kind switch
    {
        WorkflowNotificationKind.StudentResultPublished => ("STUDENT_RESULT", "PUBLISHED", "STUDENT_RESULT_PUBLISHED"),
        WorkflowNotificationKind.SupervisorReplaced => ("SUPERVISOR_ASSIGNMENT", "REPLACED", "SUPERVISOR_REPLACED"),
        WorkflowNotificationKind.EvaluatorAssigned => ("EVALUATION_ASSIGNMENT", "ACTIVE", "EVALUATOR_ASSIGNED"),
        WorkflowNotificationKind.ProjectResultPublished => ("PROJECT_RESULT", "PUBLISHED", "PROJECT_RESULT_PUBLISHED"),
        WorkflowNotificationKind.ProjectApproved => ("PROJECT_APPROVED", "APPROVED", "PROJECT_APPROVED"),
        WorkflowNotificationKind.ProjectRejected => ("PROJECT_REJECTED", "REJECTED", "PROJECT_REJECTED"),
        WorkflowNotificationKind.ProjectRevisionRequested => ("PROJECT_REVISION", "REVISION_REQUIRED", "PROJECT_REVISION_REQUESTED"),
        WorkflowNotificationKind.SupervisorFeedbackAdded => ("SUPERVISOR_FEEDBACK", "CREATED", "SUPERVISOR_FEEDBACK_ADDED"),
        WorkflowNotificationKind.EvaluationFinalized => ("EVALUATION", "FINALIZED", "EVALUATION_FINALIZED"),
        WorkflowNotificationKind.FinalSubmissionLocked => ("FINAL_SUBMISSION", "LOCKED", "FINAL_SUBMISSION_LOCKED"),
        WorkflowNotificationKind.TeamInvitationSent => ("TEAM_INVITATION", "PENDING", "TEAM_INVITATION_SENT"),
        WorkflowNotificationKind.TeamInvitationAccepted => ("TEAM_INVITATION", "ACCEPTED", "TEAM_INVITATION_ACCEPTED"),
        WorkflowNotificationKind.TeamInvitationRejected => ("TEAM_INVITATION", "REJECTED", "TEAM_INVITATION_REJECTED"),
        WorkflowNotificationKind.TeamInvitationCancelled => ("TEAM_INVITATION", "CANCELLED", "TEAM_INVITATION_CANCELLED"),
        WorkflowNotificationKind.SupervisorRequestSent => ("SUPERVISOR_REQUEST", "PENDING", "SUPERVISOR_REQUEST_SENT"),
        WorkflowNotificationKind.SupervisorRequestAccepted => ("SUPERVISOR_REQUEST", "ACCEPTED", "SUPERVISOR_REQUEST_ACCEPTED"),
        WorkflowNotificationKind.SupervisorRequestRejected => ("SUPERVISOR_REQUEST", "REJECTED", "SUPERVISOR_REQUEST_REJECTED"),
        WorkflowNotificationKind.SupervisorRequestCancelled => ("SUPERVISOR_REQUEST", "CANCELLED", "SUPERVISOR_REQUEST_CANCELLED"),
        WorkflowNotificationKind.TeamLeaderChangeRequested => ("TEAM_LEADER_CHANGE_REQUEST", "PENDING", "TEAM_LEADER_CHANGE_REQUESTED"),
        WorkflowNotificationKind.TeamLeaderChangeApproved => ("TEAM_LEADER_CHANGE_REQUEST", "APPROVED", "TEAM_LEADER_CHANGE_APPROVED"),
        WorkflowNotificationKind.TeamLeaderChangeRejected => ("TEAM_LEADER_CHANGE_REQUEST", "REJECTED", "TEAM_LEADER_CHANGE_REJECTED"),
        WorkflowNotificationKind.MeetingScheduled => ("MEETING", "SCHEDULED", "MEETING_SCHEDULED"),
        WorkflowNotificationKind.ProgressReportSubmitted => ("PROGRESS_REPORT", "SUBMITTED", "PROGRESS_REPORT_SUBMITTED"),
        WorkflowNotificationKind.ProjectSubmitted => ("PROJECT_SUBMITTED", "SUBMITTED", "PROJECT_SUBMITTED_FOR_REVIEW"),
        WorkflowNotificationKind.ProjectActivated => ("PROJECT_ACTIVATED", "ACTIVE", "PROJECT_ACTIVATED"),
        WorkflowNotificationKind.SupervisorSlotReleased => ("SUPERVISOR_SLOT_RELEASED", "REJECTED", "SUPERVISOR_SLOT_RELEASED"),
        WorkflowNotificationKind.EvaluatorRevoked => ("EVALUATION_ASSIGNMENT_REVOKED", "REVOKED", "EVALUATOR_REVOKED"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static (string Title, string Content) BuildMessage(WorkflowNotificationKind kind, bool isColdEvaluation,
        string? projectCode, string? projectTitle, string? teamName, M.Meeting? meetingSource, string? actorName)
    {
        var code = string.IsNullOrWhiteSpace(projectCode) ? null : projectCode.Trim();
        var projectFull = code is null
            ? (string.IsNullOrWhiteSpace(projectTitle) ? "của bạn" : projectTitle.Trim())
            : (string.IsNullOrWhiteSpace(projectTitle) ? code : $"{code} - {projectTitle.Trim()}");
        var codeSuffix = code is null ? "" : $" {code}";
        var team = string.IsNullOrWhiteSpace(teamName) ? "của bạn" : teamName.Trim();
        var actor = string.IsNullOrWhiteSpace(actorName) ? null : actorName.Trim();
        var byActor = actor is null ? "" : $" bởi {actor}";
        var meeting = string.IsNullOrWhiteSpace(meetingSource?.Title) ? "Cuộc họp đồ án" : meetingSource.Title.Replace('\r', ' ').Replace('\n', ' ').Trim();

        return kind switch
        {
            WorkflowNotificationKind.StudentResultPublished => (
                $"Kết quả cá nhân đồ án{codeSuffix} đã được công bố",
                $"Kết quả đánh giá cá nhân của bạn trong đồ án {projectFull} đã được công bố. Vui lòng truy cập hệ thống để xem chi tiết."),
            WorkflowNotificationKind.SupervisorReplaced => (
                $"Thay đổi giảng viên hướng dẫn đồ án{codeSuffix}",
                $"Phân công giảng viên hướng dẫn cho đồ án {projectFull} đã được cập nhật."),
            WorkflowNotificationKind.EvaluatorAssigned when isColdEvaluation => (
                $"Phân công chấm phản biện kín đồ án{codeSuffix}",
                $"Bạn đã được phân công chấm phản biện kín cho đồ án {projectFull}. Vui lòng hoàn thành trước khi xếp lịch bảo vệ."),
            WorkflowNotificationKind.EvaluatorAssigned => (
                $"Phân công chấm đánh giá đồ án{codeSuffix}",
                $"Bạn đã được phân công tham gia chấm đánh giá cho đồ án {projectFull}."),
            WorkflowNotificationKind.ProjectResultPublished => (
                $"Kết quả cuối kỳ đồ án{codeSuffix} đã được công bố",
                $"Kết quả đánh giá cuối kỳ của đồ án {projectFull} đã được công bố."),
            WorkflowNotificationKind.ProjectApproved => (
                $"Đề tài đồ án{codeSuffix} đã được phê duyệt",
                $"Đề tài đồ án {projectFull} đã được phê duyệt{byActor}."),
            WorkflowNotificationKind.ProjectRejected => (
                $"Đề tài đồ án{codeSuffix} bị từ chối",
                $"Đề tài đồ án {projectFull} đã bị từ chối{byActor}. Vui lòng xem chi tiết và trao đổi với khoa/bộ môn."),
            WorkflowNotificationKind.ProjectRevisionRequested => (
                $"Đề tài đồ án{codeSuffix} cần chỉnh sửa",
                $"Đề tài đồ án {projectFull} được yêu cầu chỉnh sửa{byActor}. Vui lòng cập nhật và nộp lại."),
            WorkflowNotificationKind.SupervisorFeedbackAdded => (
                $"Nhận xét mới từ giảng viên cho đồ án{codeSuffix}",
                $"Giảng viên{(actor is null ? "" : " " + actor)} đã thêm nhận xét mới cho đồ án {projectFull}."),
            WorkflowNotificationKind.EvaluationFinalized => (
                $"Đã hoàn tất chấm điểm đồ án{codeSuffix}",
                $"Giảng viên{(actor is null ? "" : " " + actor)} đã xác nhận hoàn tất phiếu chấm điểm cho đồ án {projectFull}."),
            WorkflowNotificationKind.FinalSubmissionLocked => (
                $"Đồ án{codeSuffix} đã khóa hồ sơ bàn giao cuối kỳ",
                $"Nhóm thực hiện đồ án {projectFull} đã nộp và khóa hồ sơ bàn giao cuối kỳ."),
            WorkflowNotificationKind.TeamInvitationSent => (
                $"Lời mời tham gia nhóm {team}",
                $"{actor ?? "Trưởng nhóm"} đã mời bạn tham gia nhóm {team}. Vui lòng kiểm tra và phản hồi lời mời."),
            WorkflowNotificationKind.TeamInvitationAccepted => (
                $"Lời mời vào nhóm {team} đã được chấp nhận",
                $"{actor ?? "Một sinh viên"} đã chấp nhận lời mời tham gia nhóm {team}."),
            WorkflowNotificationKind.TeamInvitationRejected => (
                $"Lời mời vào nhóm {team} bị từ chối",
                $"{actor ?? "Một sinh viên"} đã từ chối lời mời tham gia nhóm {team}."),
            WorkflowNotificationKind.TeamInvitationCancelled => (
                $"Lời mời tham gia nhóm {team} đã bị hủy",
                $"Lời mời tham gia nhóm {team} đã được hủy{byActor}."),
            WorkflowNotificationKind.SupervisorRequestSent => (
                $"Yêu cầu hướng dẫn đồ án{codeSuffix}",
                $"Nhóm {team} đã gửi yêu cầu mời bạn hướng dẫn đồ án {projectFull}."),
            WorkflowNotificationKind.SupervisorRequestAccepted => (
                $"Yêu cầu hướng dẫn đồ án{codeSuffix} đã được chấp nhận",
                $"Giảng viên{(actor is null ? "" : " " + actor)} đã chấp nhận hướng dẫn đồ án {projectFull}."),
            WorkflowNotificationKind.SupervisorRequestRejected => (
                $"Yêu cầu hướng dẫn đồ án{codeSuffix} bị từ chối",
                $"Giảng viên{(actor is null ? "" : " " + actor)} đã từ chối yêu cầu hướng dẫn đồ án {projectFull}."),
            WorkflowNotificationKind.SupervisorRequestCancelled => (
                $"Yêu cầu hướng dẫn đồ án{codeSuffix} đã bị hủy",
                $"Yêu cầu hướng dẫn cho đồ án {projectFull} đã được hủy."),
            WorkflowNotificationKind.TeamLeaderChangeRequested => (
                $"Yêu cầu đổi trưởng nhóm {team}",
                $"Nhóm {team} (đồ án {projectFull}) đã gửi yêu cầu thay đổi trưởng nhóm và đang chờ phê duyệt."),
            WorkflowNotificationKind.TeamLeaderChangeApproved => (
                $"Yêu cầu đổi trưởng nhóm {team} đã được duyệt",
                $"Yêu cầu thay đổi trưởng nhóm cho nhóm {team} (đồ án {projectFull}) đã được phê duyệt{byActor}."),
            WorkflowNotificationKind.TeamLeaderChangeRejected => (
                $"Yêu cầu đổi trưởng nhóm {team} bị từ chối",
                $"Yêu cầu thay đổi trưởng nhóm cho nhóm {team} (đồ án {projectFull}) đã bị từ chối{byActor}."),
            WorkflowNotificationKind.MeetingScheduled => (
                $"Lịch họp mới: {meeting}",
                meetingSource is null
                    ? $"Cuộc họp \"{meeting}\" cho đồ án {projectFull} đã được lên lịch{byActor}."
                    : $"Cuộc họp \"{meeting}\" cho đồ án {projectFull} đã được lên lịch{byActor}.\n• Thời gian: {MeetingReminderService.FormatVnTime(meetingSource.StartAt)} (Việt Nam, UTC+7)\n• Hình thức: {MeetingReminderService.FormatDeliveryLabel(meetingSource)}\n• Địa điểm: {MeetingReminderService.FormatLocation(meetingSource)}"),
            WorkflowNotificationKind.ProgressReportSubmitted => (
                $"Báo cáo tiến độ mới của đồ án{codeSuffix}",
                $"{actor ?? "Nhóm thực hiện"} đã nộp báo cáo tiến độ mới cho đồ án {projectFull}."),
            WorkflowNotificationKind.ProjectSubmitted => (
                $"Hồ sơ đề xuất đồ án{codeSuffix} chờ thẩm định",
                $"Nhóm {team} đã nộp đề xuất đồ án {projectFull} chờ Khoa/Bộ môn thẩm định."),
            WorkflowNotificationKind.ProjectActivated => (
                $"Đồ án{codeSuffix} đã chính thức kích hoạt",
                $"Đồ án {projectFull} đã hoàn tất điều kiện kích hoạt và chuyển sang trạng thái ACTIVE."),
            WorkflowNotificationKind.SupervisorSlotReleased => (
                $"Giải phóng định mức hướng dẫn đồ án{codeSuffix}",
                $"Đề xuất đồ án {projectFull} đã bị từ chối{byActor}; slot hướng dẫn của bạn đã được giải phóng tự động."),
            WorkflowNotificationKind.EvaluatorRevoked => (
                $"Thu hồi phân công chấm điểm đồ án{codeSuffix}",
                $"Phân công chấm đánh giá của bạn cho đồ án {projectFull} đã được thu hồi{byActor}."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static string Truncate(string value, int maxLength = 255) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

