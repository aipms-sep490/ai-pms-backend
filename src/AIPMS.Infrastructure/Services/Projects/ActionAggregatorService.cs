using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ActionAggregators;
using AIPMS.Application.Features.ActionAggregators.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ActionAggregatorService(AipmsDbContext db, ICurrentUser current, TimeProvider clock)
    : IActionAggregatorService
{
    private long Actor => current.UserId ?? throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<StudentNextActionsDto> StudentNextActions(CancellationToken ct)
    {
        Require(AppRoles.Student);
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Team.TeamMembers.Any(m => m.UserId == Actor && m.LeftAt == null))
            .OrderByDescending(p => p.Id)
            .Select(p => new { p.Id, p.Code, p.Title, p.Status })
            .FirstOrDefaultAsync(ct);
        if (project is null) return new(Now, null, [], 0, false);
        var items = new List<ActionAggregatorItem>();
        var now = Now;
        var taskCount = await db.Tasks.AsNoTracking()
            .LongCountAsync(t => t.DeletedAt == null && t.Milestone.ProjectId == project.Id && t.TaskAssignees.Any(a => a.UserId == Actor)
                && t.Status != "DONE" && t.Status != "CANCELLED", ct);
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt == null && t.Milestone.ProjectId == project.Id && t.TaskAssignees.Any(a => a.UserId == Actor)
                && t.Status != "DONE" && t.Status != "CANCELLED")
            .OrderByDescending(t => t.DueAt.HasValue && t.DueAt.Value < now)
            .ThenBy(t => !t.DueAt.HasValue)
            .ThenBy(t => t.DueAt).Take(20)
            .Select(t => new { t.Id, t.Title, t.Status, t.DueAt }).ToArrayAsync(ct);
        items.AddRange(tasks.Select(t => new ActionAggregatorItem("TASK", t.Id, project.Id, t.Title, t.Status,
            t.DueAt < now ? "HIGH" : "NORMAL", t.DueAt, $"/project/tasks/{t.Id}")));
        var reports = await db.ProgressReports.AsNoTracking().Where(r => r.ProjectId == project.Id && r.SubmittedBy == Actor && r.Status == "DRAFT")
            .OrderByDescending(r => r.UpdatedAt).Take(10).Select(r => new { r.Id, r.ReportType }).ToArrayAsync(ct);
        items.AddRange(reports.Select(r => new ActionAggregatorItem("PROGRESS_REPORT", r.Id, project.Id,
            $"Hoàn tất báo cáo {r.ReportType}", r.ReportType, "NORMAL", null, $"/project/progress-reports/{r.Id}")));
        var gates = await db.ProjectGateInstances.AsNoTracking().Where(g => g.ProjectId == project.Id && g.Status != "PASSED")
            .OrderBy(g => g.Sequence).Take(10).Select(g => new { g.Id, g.Code, g.Title, g.Status }).ToArrayAsync(ct);
        items.AddRange(gates.Select(g => new ActionAggregatorItem("REVIEW_GATE", g.Id, project.Id, g.Title, g.Status,
            g.Status == "BLOCKED" ? "HIGH" : "NORMAL", null, $"/project/review-gates/{g.Code}")));
        var milestones = await db.Milestones.AsNoTracking()
            .Where(m => m.ProjectId == project.Id && m.Status != "COMPLETED" && m.Status != "CANCELLED")
            .OrderByDescending(m => m.DueDate.HasValue && m.DueDate.Value < DateOnly.FromDateTime(now))
            .ThenBy(m => !m.DueDate.HasValue).ThenBy(m => m.DueDate).Take(20)
            .Select(m => new { m.Id, m.Title, m.Status, m.DueDate }).ToArrayAsync(ct);
        items.AddRange(milestones.Select(m => new ActionAggregatorItem("MILESTONE", m.Id, project.Id, m.Title, m.Status,
            m.DueDate.HasValue && m.DueDate.Value < DateOnly.FromDateTime(now) ? "HIGH" : "NORMAL",
            m.DueDate?.ToDateTime(TimeOnly.MinValue), $"/project/milestones/{m.Id}")));
        var total = taskCount + await db.ProgressReports.LongCountAsync(r => r.ProjectId == project.Id && r.SubmittedBy == Actor && r.Status == "DRAFT", ct)
            + await db.ProjectGateInstances.LongCountAsync(g => g.ProjectId == project.Id && g.Status != "PASSED", ct)
            + await db.Milestones.LongCountAsync(m => m.ProjectId == project.Id && m.Status != "COMPLETED" && m.Status != "CANCELLED", ct);
        var ordered = items.OrderByDescending(x => x.Severity == "HIGH").ThenBy(x => !x.DueAt.HasValue).ThenBy(x => x.DueAt).Take(100).ToArray();
        return new(Now, project.Id, ordered, total, total > ordered.Length);
    }

    public async Task<SupervisorTriageQueueDto> SupervisorTriage(CancellationToken ct)
    {
        Require(AppRoles.Lecturer);
        var now = Now;

        // TK-W2-14: Separate Hat 1 (PRIMARY_SUPERVISOR) and Hat 2 (DISCIPLINE_MENTOR) cleanly.
        var activeSupervisions = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.SupervisorProfile.UserId == Actor && a.EndedAt == null)
            .Select(a => new { a.ProjectId, a.AssignmentType, a.IsPrimary, a.MajorId })
            .ToArrayAsync(ct);

        var primaryProjectIds = activeSupervisions
            .Where(a => a.IsPrimary && a.AssignmentType == "PRIMARY")
            .Select(a => a.ProjectId)
            .Distinct()
            .ToArray();

        var mentorOnlyMap = activeSupervisions
            .Where(a => a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId.HasValue && !primaryProjectIds.Contains(a.ProjectId))
            .GroupBy(a => a.ProjectId)
            .ToDictionary(g => g.Key, g => g.First().MajorId!.Value);
        var mentorOnlyProjectIds = mentorOnlyMap.Keys.ToArray();
        var allSupervisedProjectIds = primaryProjectIds.Concat(mentorOnlyProjectIds).Distinct().ToArray();

        var items = new List<ActionAggregatorItem>();

        // 1. Progress reports awaiting review (Hat 1 + Hat 2 with role-accurate deep links)
        var reportCount = allSupervisedProjectIds.Length == 0
            ? 0L
            : await db.ProgressReports.AsNoTracking()
                .LongCountAsync(r => allSupervisedProjectIds.Contains(r.ProjectId) && r.Status == "SUBMITTED", ct);
        if (reportCount > 0)
        {
            var reports = await db.ProgressReports.AsNoTracking()
                .Where(r => allSupervisedProjectIds.Contains(r.ProjectId) && r.Status == "SUBMITTED")
                .OrderBy(r => r.SubmittedAt).Take(50)
                .Select(r => new { r.Id, r.ProjectId, r.ReportType, r.SubmittedAt })
                .ToArrayAsync(ct);
            items.AddRange(reports.Select(r =>
            {
                var link = primaryProjectIds.Contains(r.ProjectId)
                    ? $"/supervisor/projects/{r.ProjectId}/reports/{r.Id}"
                    : $"/mentor/projects/{r.ProjectId}/majors/{mentorOnlyMap[r.ProjectId]}/reports/{r.Id}";
                return new ActionAggregatorItem("PROGRESS_REVIEW", r.Id, r.ProjectId,
                    $"Duyệt báo cáo {r.ReportType}", "SUBMITTED", "HIGH", r.SubmittedAt, link);
            }));
        }

        // 2. Incoming supervisor invitations addressed directly to Actor (non-expired PENDING)
        var pendingRequestsQuery = db.SupervisorRequests.AsNoTracking()
            .Where(r => r.SupervisorProfile.UserId == Actor
                && r.Status == "PENDING"
                && (r.ExpiresAt == null || r.ExpiresAt > now));
        var requestCount = await pendingRequestsQuery.LongCountAsync(ct);
        if (requestCount > 0)
        {
            var requests = await pendingRequestsQuery
                .OrderBy(r => r.RequestedAt).Take(50)
                .Select(r => new { r.Id, r.ProjectId, r.AssignmentType, r.RequestedAt, r.ExpiresAt })
                .ToArrayAsync(ct);
            items.AddRange(requests.Select(r => new ActionAggregatorItem("SUPERVISOR_REQUEST", r.Id, r.ProjectId,
                $"Xử lý lời mời {r.AssignmentType}", "PENDING", "NORMAL", r.ExpiresAt ?? r.RequestedAt,
                $"/supervisor/workspace?tab=requests&requestId={r.Id}")));
        }

        // 3. Completed meetings awaiting unlocked notes sign-off (Hat 1 Primary only; checks NotesLockedAt == null)
        var meetingCount = primaryProjectIds.Length == 0
            ? 0L
            : await db.Meetings.AsNoTracking()
                .LongCountAsync(m => primaryProjectIds.Contains(m.ProjectId) && m.Status == "COMPLETED" && m.NotesLockedAt == null, ct);
        if (meetingCount > 0)
        {
            var meetings = await db.Meetings.AsNoTracking()
                .Where(m => primaryProjectIds.Contains(m.ProjectId) && m.Status == "COMPLETED" && m.NotesLockedAt == null)
                .OrderBy(m => m.StartAt).Take(50)
                .Select(m => new { m.Id, m.ProjectId, m.Title, m.StartAt })
                .ToArrayAsync(ct);
            items.AddRange(meetings.Select(m => new ActionAggregatorItem("MEETING_SIGN_OFF", m.Id, m.ProjectId,
                $"Khóa biên bản họp: {m.Title}", "COMPLETED", "NORMAL", m.StartAt,
                $"/supervisor/projects/{m.ProjectId}/meetings/{m.Id}")));
        }

        // 4. Hat 3: Assigned Gate Reviews (PENDING_REVIEW / IN_REVIEW within valid window)
        var gateAssignQuery = db.GateReviewerAssignments.AsNoTracking()
            .Where(a => a.ReviewerId == Actor
                && (a.Status == "PENDING_REVIEW" || a.Status == "IN_REVIEW")
                && (a.ValidFrom == null || a.ValidFrom <= now)
                && (a.ValidUntil == null || now <= a.ValidUntil));
        var gateAssignCount = await gateAssignQuery.LongCountAsync(ct);
        if (gateAssignCount > 0)
        {
            var gateAssigns = await gateAssignQuery
                .OrderBy(a => a.GateInstance.ReviewWindowEndAt ?? a.GateInstance.WindowEnd ?? a.ValidUntil)
                .Take(50)
                .Select(a => new
                {
                    a.Id,
                    a.GateInstance.ProjectId,
                    a.GateInstance.Code,
                    a.GateInstance.Title,
                    a.Status,
                    DueAt = a.GateInstance.ReviewWindowEndAt ?? a.GateInstance.WindowEnd ?? a.ValidUntil
                })
                .ToArrayAsync(ct);
            items.AddRange(gateAssigns.Select(g => new ActionAggregatorItem("GATE_REVIEW_ASSIGNMENT", g.Id, g.ProjectId,
                $"Thẩm định cổng {g.Code}: {g.Title}", g.Status, "HIGH", g.DueAt,
                $"/review/gate-assignments?assignmentId={g.Id}")));
        }

        // 5. Hat 4: 80% Evaluation Assignments with real DueAt from ValidUntil or ProjectPeriod.EndAt
        var evalQuery = db.Set<EvaluationAssignment>().AsNoTracking()
            .Join(db.ProjectPeriods.AsNoTracking(), a => a.ProjectPeriodId, p => p.Id, (a, p) => new { Assignment = a, Period = p })
            .Where(x => x.Assignment.EvaluatorId == Actor
                && x.Assignment.Status != "REVOKED"
                && x.Assignment.Status != "FINALIZED"
                && (x.Assignment.ValidFrom == null || x.Assignment.ValidFrom <= now)
                && (x.Assignment.ValidUntil == null || now <= x.Assignment.ValidUntil));
        var evalCount = await evalQuery.LongCountAsync(ct);
        if (evalCount > 0)
        {
            var evalAssigns = await evalQuery
                .OrderBy(x => x.Assignment.ValidUntil ?? x.Period.EndAt)
                .ThenBy(x => x.Assignment.Id)
                .Take(50)
                .Select(x => new
                {
                    x.Assignment.Id,
                    x.Assignment.ProjectId,
                    x.Assignment.Stage,
                    x.Assignment.Scope,
                    x.Assignment.Status,
                    DueAt = (DateTime?)(x.Assignment.ValidUntil ?? x.Period.EndAt)
                })
                .ToArrayAsync(ct);
            items.AddRange(evalAssigns.Select(a => new ActionAggregatorItem("EVALUATION_ASSIGNMENT", a.Id, a.ProjectId,
                $"Chấm {a.Stage} ({a.Scope}) · #{a.Id}", a.Status, "HIGH", a.DueAt,
                $"/evaluator/assignments/{a.Id}")));
        }

        // 6. Blocked tasks > 48h (Hat 1 whole project; Hat 2 scoped to mentor's own major + COMMON/INTEGRATION)
        var blockedCutoff = now.AddHours(-48);
        var primaryBlockedCount = primaryProjectIds.Length == 0
            ? 0L
            : await db.Tasks.AsNoTracking()
                .LongCountAsync(t => t.DeletedAt == null && primaryProjectIds.Contains(t.Milestone.ProjectId)
                    && t.Status == "BLOCKED" && t.BlockedSinceAt.HasValue && t.BlockedSinceAt.Value <= blockedCutoff, ct);
        if (primaryBlockedCount > 0)
        {
            var primaryBlocked = await db.Tasks.AsNoTracking()
                .Where(t => t.DeletedAt == null && primaryProjectIds.Contains(t.Milestone.ProjectId)
                    && t.Status == "BLOCKED" && t.BlockedSinceAt.HasValue && t.BlockedSinceAt.Value <= blockedCutoff)
                .OrderBy(t => t.BlockedSinceAt).Take(30)
                .Select(t => new { t.Id, t.Milestone.ProjectId, t.Title, t.BlockedSinceAt })
                .ToArrayAsync(ct);
            items.AddRange(primaryBlocked.Select(t => new ActionAggregatorItem("TASK_BLOCKED_48H", t.Id, t.ProjectId,
                $"Task BLOCKED > 48h: {t.Title}", "BLOCKED", "HIGH", t.BlockedSinceAt,
                $"/supervisor/projects/{t.ProjectId}/tasks/{t.Id}")));
        }

        var mentorBlockedCount = 0L;
        if (mentorOnlyProjectIds.Length > 0)
        {
            var mentorCandidates = await db.Tasks.AsNoTracking()
                .Where(t => t.DeletedAt == null && mentorOnlyProjectIds.Contains(t.Milestone.ProjectId)
                    && t.Status == "BLOCKED" && t.BlockedSinceAt.HasValue && t.BlockedSinceAt.Value <= blockedCutoff)
                .OrderBy(t => t.BlockedSinceAt)
                .Select(t => new
                {
                    t.Id,
                    t.Milestone.ProjectId,
                    t.Title,
                    t.BlockedSinceAt
                })
                .ToArrayAsync(ct);

            var candidateTaskIds = mentorCandidates.Select(t => t.Id).ToArray();
            var taskMajorLookup = candidateTaskIds.Length == 0
                ? new Dictionary<long, long[]>()
                : (await db.TaskDisciplines.AsNoTracking()
                    .Where(d => candidateTaskIds.Contains(d.TaskId))
                    .Select(d => new { d.TaskId, d.MajorId })
                    .ToArrayAsync(ct))
                    .GroupBy(x => x.TaskId)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.MajorId).ToArray());

            var scopedMentorBlocked = mentorCandidates
                .Where(t =>
                {
                    var majors = taskMajorLookup.GetValueOrDefault(t.Id);
                    return majors is null || majors.Length == 0 || majors.Contains(mentorOnlyMap[t.ProjectId]);
                })
                .ToArray();
            mentorBlockedCount = scopedMentorBlocked.LongLength;
            items.AddRange(scopedMentorBlocked.Take(30).Select(t => new ActionAggregatorItem("TASK_BLOCKED_48H", t.Id, t.ProjectId,
                $"Task BLOCKED > 48h: {t.Title}", "BLOCKED", "HIGH", t.BlockedSinceAt,
                $"/mentor/projects/{t.ProjectId}/majors/{mentorOnlyMap[t.ProjectId]}/tasks/{t.Id}")));
        }

        // 7. Pending Final Lock approvals (Hat 1 Primary ONLY — never Mentor)
        var pendingLockCount = primaryProjectIds.Length == 0
            ? 0L
            : await db.Set<FinalSubmissionDraft>().AsNoTracking()
                .LongCountAsync(d => primaryProjectIds.Contains(d.ProjectId) && d.LockWorkflowStatus == "PENDING_SUPERVISOR_LOCK", ct);
        if (pendingLockCount > 0)
        {
            var pendingLocks = await db.Set<FinalSubmissionDraft>().AsNoTracking()
                .Where(d => primaryProjectIds.Contains(d.ProjectId) && d.LockWorkflowStatus == "PENDING_SUPERVISOR_LOCK")
                .OrderBy(d => d.LockRequestedAt).Take(20)
                .Select(d => new { d.Id, d.ProjectId, d.LockRequestedAt })
                .ToArrayAsync(ct);
            items.AddRange(pendingLocks.Select(d => new ActionAggregatorItem("FINAL_LOCK_APPROVAL", d.Id, d.ProjectId,
                "Ký khóa hồ sơ Tiền-Defense (Final & Cold)", "PENDING_SUPERVISOR_LOCK", "HIGH", d.LockRequestedAt,
                $"/supervisor/projects/{d.ProjectId}/final-submission")));
        }

        // 8. Hat 5: Confirmed Defense Committees (works both with room session and with POL-06 roomless mode)
        var committeeQuery = db.Set<DefenseCommitteeMember>().AsNoTracking()
            .Join(db.Set<DefenseCommittee>().AsNoTracking(), m => m.CommitteeId, c => c.Id, (m, c) => new { Member = m, Committee = c })
            .Where(x => x.Member.UserId == Actor && x.Committee.Status == "CONFIRMED");
        var committeeCount = await committeeQuery.LongCountAsync(ct);
        if (committeeCount > 0)
        {
            var committees = await committeeQuery
                .OrderBy(x => x.Committee.Id).Take(30)
                .Select(x => new { x.Committee.Id, x.Committee.ProjectId, x.Committee.Name, x.Member.Role })
                .ToArrayAsync(ct);
            var committeeIds = committees.Select(c => c.Id).ToArray();
            var scheduledSessions = await db.Set<DefenseSession>().AsNoTracking()
                .Where(s => committeeIds.Contains(s.CommitteeId) && s.Status == "SCHEDULED")
                .OrderBy(s => s.StartAt)
                .Select(s => new { s.Id, s.CommitteeId, s.StartAt })
                .ToArrayAsync(ct);
            var sessionByCommittee = scheduledSessions
                .GroupBy(s => s.CommitteeId)
                .ToDictionary(g => g.Key, g => g.First());

            items.AddRange(committees.Select(c =>
            {
                var hasSession = sessionByCommittee.TryGetValue(c.Id, out var session);
                var deepLink = hasSession ? $"/lecturer/defense-sessions/{session!.Id}" : "/evaluator/workspace";
                DateTime? dueAt = hasSession ? session!.StartAt : null;
                return new ActionAggregatorItem("DEFENSE_COMMITTEE", c.Id, c.ProjectId,
                    $"Hội đồng bảo vệ ({c.Role}): {c.Name}", "CONFIRMED", "HIGH", dueAt, deepLink);
            }));
        }

        var ordered = items
            .OrderByDescending(x => x.Severity == "HIGH")
            .ThenBy(x => !x.DueAt.HasValue)
            .ThenBy(x => x.DueAt)
            .Take(100)
            .ToArray();
        var total = reportCount + requestCount + meetingCount + gateAssignCount + evalCount
            + primaryBlockedCount + mentorBlockedCount + pendingLockCount + committeeCount;
        return new(Now, ordered, total, total > ordered.Length);
    }

    public async Task<DepartmentUrgentActionsDto> DepartmentUrgent(long departmentId, CancellationToken ct)
    {
        Require(AppRoles.DepartmentStaff);
        var scope = await db.Users.AsNoTracking().Where(u => u.Id == Actor && u.DepartmentId == departmentId && u.Status == "ACTIVE").AnyAsync(ct);
        if (!scope && !current.Roles.Contains(AppRoles.Admin)) throw new ForbiddenException("Department scope is required.");
        var projectIds = db.ProjectMajors.AsNoTracking().Where(x => x.Major.DepartmentId == departmentId).Select(x => x.ProjectId).Distinct();
        var items = new List<ActionAggregatorItem>();
        var unassignedCount = await db.ProjectGateInstances.AsNoTracking().LongCountAsync(g => projectIds.Contains(g.ProjectId) && g.Status != "PASSED"
            && !g.Assignments.Any(a => a.RevokedAt == null && a.Status != "REVOKED"), ct);
        var unassigned = await db.ProjectGateInstances.AsNoTracking().Where(g => projectIds.Contains(g.ProjectId) && g.Status != "PASSED"
            && !g.Assignments.Any(a => a.RevokedAt == null && a.Status != "REVOKED"))
            .OrderBy(g => g.Sequence).Take(50).Select(g => new { g.Id, g.ProjectId, g.Title, g.Status }).ToArrayAsync(ct);
        items.AddRange(unassigned.Select(g => new ActionAggregatorItem("REVIEWER_ASSIGNMENT", g.Id, g.ProjectId,
            $"Chưa phân reviewer: {g.Title}", g.Status, "HIGH", null, $"/department/projects/{g.ProjectId}/review-gates")));
        var appealCount = await db.Set<ResultAppealRequest>().AsNoTracking().LongCountAsync(a => projectIds.Contains(a.ProjectId) && (a.Status == "PENDING" || a.Status == "UNDER_REVIEW"), ct);
        var appeals = await db.Set<ResultAppealRequest>().AsNoTracking().Where(a => projectIds.Contains(a.ProjectId) && (a.Status == "PENDING" || a.Status == "UNDER_REVIEW"))
            .OrderBy(a => a.RequestedAt).Take(50).Select(a => new { a.Id, a.ProjectId, a.Status, a.RequestedAt }).ToArrayAsync(ct);
        items.AddRange(appeals.Select(a => new ActionAggregatorItem("APPEAL", a.Id, a.ProjectId,
            "Phúc khảo/đính chính cần xử lý", a.Status, "HIGH", a.RequestedAt, $"/department/projects/{a.ProjectId}/appeals/{a.Id}")));
        var ordered = items.OrderByDescending(x => x.Severity == "HIGH").ThenBy(x => !x.DueAt.HasValue).ThenBy(x => x.DueAt).Take(100).ToArray();
        var total = unassignedCount + appealCount;
        return new(Now, departmentId, ordered, total, total > ordered.Length);
    }

    private void Require(string role)
    {
        if (!current.IsAuthenticated) throw new UnauthorizedException();
        if (!current.Roles.Contains(role) && !current.Roles.Contains(AppRoles.Admin)) throw new ForbiddenException();
    }
}
