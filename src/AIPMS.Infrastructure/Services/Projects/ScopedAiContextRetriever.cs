using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class AiEvidenceCapture
{
    public string? ScopeHash { get; set; }
    public IReadOnlyList<EvidenceReferenceDto> Evidence { get; set; } = [];
}

internal sealed class ScopedAiContextRetriever(AipmsDbContext db, AiActorScope access, TimeProvider clock, AiEvidenceCapture capture) : IAiContextRetriever
{
    private static string Text(string? value, int max = 350) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Length > max ? value.Trim()[..max] + "..." : value.Trim();

    private static string[] ExtractQueryTerms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return Regex.Matches(query.ToLowerInvariant(), @"[\p{L}\p{N}]{2,}")
            .Select(m => m.Value).Distinct().Take(10).ToArray();
    }

    public async Task<ProjectBoundedContext> RetrieveProjectContextAsync(long projectId, string query, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var now = clock.GetUtcNow().UtcDateTime;
        var terms = ExtractQueryTerms(query);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var scope = await access.Read(projectId, ct);
        var evidence = new List<EvidenceReferenceDto>();

        // 1. Project Overview (profile, majors, team members, supervisors)
        var projectMeta = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.Code, p.Title, p.Description, p.ProblemStatement, p.Objectives, p.ExpectedOutput, p.TeamId
            }).SingleAsync(ct);

        var majorList = await db.ProjectMajors.AsNoTracking()
            .Where(pm => pm.ProjectId == projectId)
            .Select(pm => new { pm.MajorId, pm.Major.Name })
            .ToArrayAsync(ct);
        var majorReqs = await db.ProjectMajorRequirements.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .Select(r => new { r.MajorId, r.Responsibility })
            .ToDictionaryAsync(r => r.MajorId, r => r.Responsibility, ct);
        var majorsOverview = majorList
            .Where(m => scope.Whole || scope.Majors.Contains(m.MajorId))
            .Select(m => new ProjectMajorOverviewItem(m.MajorId, m.Name,
                majorReqs.TryGetValue(m.MajorId, out var resp) ? Text(resp, 240) : null))
            .ToArray();

        var membersOverview = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == projectMeta.TeamId && m.LeftAt == null)
            .Select(m => new { m.UserId, m.User.FullName, MajorName = m.User.Major == null ? null : m.User.Major.Name, m.IsLeader })
            .ToArrayAsync(ct);
        var majorNameMap = majorList.ToDictionary(m => m.MajorId, m => m.Name);
        var supervisorsOverview = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.EndedAt == null)
            .Select(a => new
            {
                UserId = a.SupervisorProfile.UserId,
                a.SupervisorProfile.User.FullName,
                a.AssignmentType,
                a.MajorId
            }).ToArrayAsync(ct);

        var isStudentMember = membersOverview.Any(m => m.UserId == scope.Actor);
        var actorMajorId = isStudentMember
            ? await db.Users.AsNoTracking().Where(u => u.Id == scope.Actor).Select(u => u.MajorId).SingleOrDefaultAsync(ct)
            : null;

        var overview = new ProjectOverviewEvidence(
            projectMeta.Code,
            Text(projectMeta.Title, 250),
            Text(projectMeta.Description, 450),
            Text(projectMeta.ProblemStatement, 450),
            Text(projectMeta.Objectives, 450),
            Text(projectMeta.ExpectedOutput, 450),
            majorList.Length > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR",
            majorsOverview,
            membersOverview.Select(m => new ProjectMemberOverviewItem(m.UserId, m.FullName, m.MajorName, m.IsLeader)).ToArray(),
            supervisorsOverview.Select(s => new ProjectSupervisorOverviewItem(s.UserId, s.FullName, s.AssignmentType,
                s.MajorId.HasValue && majorNameMap.TryGetValue(s.MajorId.Value, out var smn) ? smn : null)).ToArray());

        // 2. Tasks with full diagnostic details (Milestone, Assignees, Majors, Criteria, Dependencies, Impacts, Files, Latest Comment)
        var tasksQuery = access.Tasks(scope);
        var totalTasks = await tasksQuery.CountAsync(ct);
        var doneTasksTotal = await tasksQuery.CountAsync(t => t.Status == "DONE" || t.Status == "COMPLETED", ct);
        var blockedTasksTotal = await tasksQuery.CountAsync(t => t.Status == "BLOCKED", ct);
        var overdueTasksTotal = await tasksQuery.CountAsync(t => t.DueAt != null && t.DueAt < now && t.Status != "DONE" && t.Status != "COMPLETED" && t.Status != "CANCELLED", ct);

        var visibleTaskIdsQuery = tasksQuery.Select(t => t.Id);
        var pendingImpactsRaw = await db.TaskDependencyImpacts.AsNoTracking()
            .Where(i => visibleTaskIdsQuery.Contains(i.TaskId) && (i.Status == "PENDING" || i.Status == "REWORK_REQUIRED"))
            .Select(i => new { i.TaskId, i.Status, PredecessorTitle = i.PredecessorTask.Title, i.ReopenReason })
            .Take(100).ToArrayAsync(ct);
        var impactsByTask = pendingImpactsRaw.ToLookup(i => i.TaskId);
        var impactTaskIds = impactsByTask.Select(g => g.Key).ToHashSet();

        var rawTasks = await tasksQuery
            .Select(t => new
            {
                t.Id, t.MilestoneId, MilestoneTitle = scope.Whole ? t.Milestone.Title : null, t.Title, t.Status, t.Priority, t.DueAt, t.Description,
                FileCount = t.Files.Count,
                Assignees = t.TaskAssignees.Select(a => a.User.FullName).ToArray()
            })
            .Take(150).ToArrayAsync(ct);

        var prioritizedTasks = rawTasks
            .Select(t =>
            {
                var overdue = t.DueAt.HasValue && t.DueAt < now && t.Status is not ("DONE" or "COMPLETED" or "CANCELLED");
                var blocked = t.Status == "BLOCKED";
                var hasImpact = impactTaskIds.Contains(t.Id);
                var queryMatch = terms.Length > 0 && terms.Any(term =>
                    t.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (t.Description != null && t.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
                    || (t.MilestoneTitle != null && t.MilestoneTitle.Contains(term, StringComparison.OrdinalIgnoreCase)));
                var priorityRank = (queryMatch ? 0 : 10)
                    + (blocked ? 0 : overdue ? 1 : hasImpact ? 2 : t.Status is "IN_PROGRESS" or "TODO" ? 4 : 8);
                return new { Task = t, Overdue = overdue, Blocked = blocked, PriorityRank = priorityRank };
            })
            .OrderBy(x => x.PriorityRank)
            .ThenBy(x => x.Task.DueAt ?? DateTime.MaxValue)
            .ThenBy(x => x.Task.Id)
            .Take(50)
            .ToArray();

        var selectedTaskIds = prioritizedTasks.Select(x => x.Task.Id).ToArray();
        var taskDisciplines = selectedTaskIds.Length == 0 ? [] : await (
            from td in db.TaskDisciplines.AsNoTracking()
            join m in db.Majors.AsNoTracking() on td.MajorId equals m.Id
            where selectedTaskIds.Contains(td.TaskId)
            select new { td.TaskId, m.Name }).ToArrayAsync(ct);
        var disciplinesByTask = taskDisciplines.ToLookup(x => x.TaskId, x => x.Name);

        var taskDeps = selectedTaskIds.Length == 0 ? [] : await db.TaskDependencies.AsNoTracking()
            .Where(d => selectedTaskIds.Contains(d.TaskId)
                && visibleTaskIdsQuery.Contains(d.DependsOnTaskId)
                && d.DependsOnTask.Status != "DONE" && d.DependsOnTask.Status != "COMPLETED" && d.DependsOnTask.Status != "CANCELLED")
            .Select(d => new { d.TaskId, BlockerTitle = d.DependsOnTask.Title, BlockerStatus = d.DependsOnTask.Status })
            .ToArrayAsync(ct);
        var blockersByTask = taskDeps.ToLookup(x => x.TaskId, x => $"{x.BlockerTitle} ({x.BlockerStatus})");

        var taskCriteriaStats = selectedTaskIds.Length == 0 ? [] : await db.TaskCriteria.AsNoTracking()
            .Where(c => selectedTaskIds.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { TaskId = g.Key, Total = g.Count(), Completed = g.Count(c => c.IsCompleted) })
            .ToArrayAsync(ct);
        var criteriaByTask = taskCriteriaStats.ToDictionary(x => x.TaskId);

        var latestComments = selectedTaskIds.Length == 0 ? [] : await db.TaskComments.AsNoTracking()
            .Where(c => selectedTaskIds.Contains(c.TaskId))
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Select(c => new { c.TaskId, c.Content, Author = c.Author.FullName })
            .Take(100).ToArrayAsync(ct);
        var commentByTask = latestComments.GroupBy(c => c.TaskId)
            .ToDictionary(g => g.Key, g => $"{g.First().Author}: {Text(g.First().Content, 160)}");

        var taskItems = prioritizedTasks.Select(x =>
        {
            var t = x.Task;
            var blockers = blockersByTask[t.Id].Distinct().Take(5).ToArray();
            var impacts = impactsByTask[t.Id].ToArray();
            var impactSummary = impacts.Length == 0 ? null
                : string.Join("; ", impacts.Take(2).Select(i => $"[{i.Status}] ảnh hưởng từ \"{i.PredecessorTitle}\": {Text(i.ReopenReason, 120)}"));
            criteriaByTask.TryGetValue(t.Id, out var crit);
            commentByTask.TryGetValue(t.Id, out var latestComment);
            return new TaskEvidenceItem(
                $"TASK-{t.Id}", t.Id, Text(t.Title, 200), t.Status, t.Priority,
                t.DueAt?.ToString("O"), x.Overdue, x.Blocked, Text(t.Description, 280),
                scope.Whole ? t.MilestoneId : null, scope.Whole ? Text(t.MilestoneTitle, 150) : null,
                t.Assignees, disciplinesByTask[t.Id].Distinct().ToArray(),
                t.FileCount, crit?.Total ?? 0, crit?.Completed ?? 0,
                blockers, impacts.Length, impactSummary, latestComment);
        }).ToArray();

        foreach (var t in taskItems)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(t.MilestoneTitle)) parts.Add($"Mốc: {t.MilestoneTitle}");
            parts.Add($"Trạng thái: {t.Status}");
            if (t.IsOverdue) parts.Add("QUÁ HẠN");
            if (t.IsBlocked) parts.Add("BỊ CHẶN");
            if (t.AssigneeNames is { Count: > 0 }) parts.Add($"Phụ trách: {string.Join(", ", t.AssigneeNames)}");
            if (t.MajorNames is { Count: > 0 }) parts.Add($"Ngành: {string.Join(", ", t.MajorNames)}");
            if (t.CriteriaTotal > 0) parts.Add($"Tiêu chí: {t.CriteriaCompleted}/{t.CriteriaTotal}");
            parts.Add($"Tệp minh chứng: {t.FileCount}");
            if (t.BlockingTaskTitles is { Count: > 0 }) parts.Add($"Chờ việc: {string.Join(", ", t.BlockingTaskTitles)}");
            if (!string.IsNullOrWhiteSpace(t.ImpactSummary)) parts.Add($"Tác động mở lại: {t.ImpactSummary}");
            if (!string.IsNullOrWhiteSpace(t.LatestComment)) parts.Add($"Bình luận mới nhất: {t.LatestComment}");
            if (!string.IsNullOrWhiteSpace(t.Description)) parts.Add(t.Description);
            evidence.Add(new("TASK", t.Id, t.Title, t.DueAt, $"/api/v1/tasks/{t.TaskId}", string.Join(" | ", parts)));
        }

        // 3. Deliverables & Latest Versions & Supervisor Feedbacks (strictly scoped; COMMON + INTEGRATION visible to mentors)
        var deliverablesQuery = db.Deliverables.AsNoTracking().Where(d => d.ProjectId == projectId &&
            (scope.Whole || (d.TargetMajorId != null && scope.Majors.Contains(d.TargetMajorId.Value))
                || ((d.EvidenceScope == "COMMON" || d.EvidenceScope == "INTEGRATION" || d.EvidenceScope == null) && d.TargetMajorId == null && d.TargetStudentId == null)) &&
            (!isStudentMember || ((d.TargetStudentId == null || d.TargetStudentId == scope.Actor)
                && (d.TargetMajorId == null || actorMajorId == d.TargetMajorId))));
        var rawDeliverables = await deliverablesQuery
            .OrderBy(d => d.DueAt == null).ThenBy(d => d.DueAt).ThenBy(d => d.Id)
            .Select(d => new
            {
                d.Id, d.Title, d.DeliverableType, d.EvidenceScope, d.TargetMajorId, d.Status, d.DueAt,
                MilestoneTitle = !scope.Whole || d.Milestone == null ? null : d.Milestone.Title,
                LatestVersion = d.DeliverableVersions.OrderByDescending(v => v.VersionNumber)
                    .Select(v => new
                    {
                        v.VersionNumber, v.Status, v.SubmittedAt,
                        Files = v.Files.Select(f => f.OriginalFileName).ToArray(),
                        Feedback = v.SupervisorFeedbacks.OrderByDescending(f => f.CreatedAt).Select(f => f.FeedbackText).FirstOrDefault()
                    }).FirstOrDefault()
            }).Take(25).ToArrayAsync(ct);

        var deliverableItems = rawDeliverables.Select(d =>
        {
            var isOverdue = d.DueAt.HasValue && d.DueAt < now && d.Status is not ("ACCEPTED" or "APPROVED" or "CLOSED");
            var targetMajorName = d.TargetMajorId.HasValue && majorNameMap.TryGetValue(d.TargetMajorId.Value, out var mn) ? mn : null;
            return new DeliverableEvidenceItem(
                $"DEL-{d.Id}", d.Id, Text(d.Title, 200), d.DeliverableType, Text(d.MilestoneTitle, 150),
                d.EvidenceScope ?? "COMMON", targetMajorName, d.Status, d.DueAt?.ToString("O"), isOverdue,
                d.LatestVersion?.VersionNumber ?? 0, d.LatestVersion?.Status, d.LatestVersion?.SubmittedAt.ToString("O"),
                d.LatestVersion?.Files ?? [], Text(d.LatestVersion?.Feedback, 240));
        }).ToArray();

        foreach (var d in deliverableItems)
        {
            var versionInfo = d.LatestVersionNumber > 0
                ? $"v{d.LatestVersionNumber} ({d.LatestVersionStatus}, {d.FileNames.Count} tệp: {string.Join(", ", d.FileNames.Take(3))})"
                : "Chưa nộp phiên bản nào";
            var parts = new List<string>
            {
                $"Hạng mục bàn giao ({d.DeliverableType ?? "Tài liệu"})",
                string.IsNullOrWhiteSpace(d.MilestoneTitle) ? "Chưa gắn mốc" : $"Mốc: {d.MilestoneTitle}",
                $"Trạng thái: {d.Status}{(d.IsOverdue ? " (QUÁ HẠN)" : "")}",
                $"Phiên bản mới nhất: {versionInfo}"
            };
            if (!string.IsNullOrWhiteSpace(d.TargetMajorName)) parts.Add($"Ngành: {d.TargetMajorName}");
            if (!string.IsNullOrWhiteSpace(d.LatestFeedback)) parts.Add($"Nhận xét GVHD: {d.LatestFeedback}");
            evidence.Add(new("DELIVERABLE", d.Id, d.Title, d.DueAt, $"/api/v1/deliverables/{d.DeliverableId}", string.Join(" | ", parts)));
        }

        // 4. Cold Report Checklist & Submissions (Chấm nguội)
        var coldPackage = await db.Set<ColdPackage>().AsNoTracking()
            .Where(p => p.ProjectId == projectId).OrderByDescending(p => p.Id).FirstOrDefaultAsync(ct);
        var coldItems = new List<ColdReportEvidenceItem>();
        if (coldPackage is not null)
        {
            var rawCold = await db.Set<ColdChecklistItem>().AsNoTracking()
                .Where(c => c.PackageId == coldPackage.Id
                    && (scope.Whole || c.Scope == "COMMON" || c.Scope == "INTEGRATION" || (c.MajorId != null && scope.Majors.Contains(c.MajorId.Value)))
                    && (!isStudentMember || ((c.StudentId == null || c.StudentId == scope.Actor) && (c.MajorId == null || actorMajorId == c.MajorId))))
                .OrderBy(c => c.Id).Take(25).ToArrayAsync(ct);
            var coldIds = rawCold.Select(c => c.Id).ToArray();
            var coldSubs = coldIds.Length == 0 ? [] : await db.Set<ColdSubmission>().AsNoTracking()
                .Include(s => s.Files)
                .Where(s => coldIds.Contains(s.ChecklistItemId))
                .OrderByDescending(s => s.Version).ThenByDescending(s => s.Id)
                .ToArrayAsync(ct);
            var latestSubByItem = coldSubs.GroupBy(s => s.ChecklistItemId).ToDictionary(g => g.Key, g => g.First());
            foreach (var c in rawCold)
            {
                latestSubByItem.TryGetValue(c.Id, out var sub);
                var subStatus = sub?.Status ?? "NOT_STARTED";
                var files = sub?.Files.Select(f => f.FileName).ToArray() ?? [];
                var majorName = c.MajorId.HasValue && majorNameMap.TryGetValue(c.MajorId.Value, out var mn) ? mn : null;
                var item = new ColdReportEvidenceItem($"COLD-{c.Id}", c.Id, c.ReportType, Text(c.Name, 200),
                    c.Scope, majorName, c.Required, sub?.Version ?? 0, subStatus, files.Length, files,
                    coldPackage.Deadline.ToString("O"), Text(sub?.Note, 200));
                coldItems.Add(item);
                var desc = $"Chấm nguội ({c.ReportType}{(c.Required ? ", Bắt buộc" : "")}) | Trạng thái: {subStatus} | v{item.LatestVersion} ({files.Length} tệp{(files.Length > 0 ? ": " + string.Join(", ", files.Take(2)) : "")})"
                    + (majorName != null ? $" | Ngành: {majorName}" : "")
                    + (!string.IsNullOrWhiteSpace(item.Note) ? $" | Ghi chú: {item.Note}" : "");
                evidence.Add(new("COLD_REPORT", item.Id, item.Name, item.Deadline, $"/api/v1/projects/{projectId}/cold/checklist", desc));
            }
        }

        // 5. Review Gates & Requirements & Reviewer Feedback
        var rawGates = await db.Set<ProjectGateInstance>().AsNoTracking()
            .Include(g => g.Requirements).ThenInclude(r => r.Evidence)
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.Sequence).ThenBy(g => g.Id)
            .Take(6).ToArrayAsync(ct);
        var gateItems = new List<ReviewGateEvidenceItem>();
        foreach (var g in rawGates)
        {
            var gateFeedbackVisible = scope.IsStaff || g.Status is "PASSED" or "REVISION_REQUIRED" or "FAILED" or "OVERRIDDEN_PASSED" or "OVERRIDDEN_FAILED";
            var visibleReqs = g.Requirements
                .Where(r => (scope.Whole || r.Scope == "COMMON" || r.Scope == "INTEGRATION" || (r.MajorId != null && scope.Majors.Contains(r.MajorId.Value)))
                    && (!isStudentMember || ((r.TargetStudentId == null || r.TargetStudentId == scope.Actor) && (r.MajorId == null || actorMajorId == r.MajorId))))
                .OrderBy(r => r.Id)
                .Select(r =>
                {
                    var latestEv = r.Evidence.OrderByDescending(e => e.Version).ThenByDescending(e => e.Id).FirstOrDefault();
                    var safeFeedback = gateFeedbackVisible ? Text(r.ReviewerFeedback, 220) : "";
                    return new ReviewGateRequirementEvidenceItem(r.Id, r.Code, Text(r.Title, 180), r.Type, r.Scope,
                        r.Required, r.VerificationStatus, latestEv?.ArtifactName, safeFeedback);
                }).ToArray();
            if (visibleReqs.Length == 0 && !scope.Whole) continue;
            var gateDto = new ReviewGateEvidenceItem($"GATE-{g.Code}", g.Code, Text(g.Title, 180), g.Status,
                g.ReviewEligibility, g.WindowEnd?.ToString("O"), visibleReqs);
            gateItems.Add(gateDto);
            var reqSummaries = visibleReqs.Select(r =>
                $"{r.Title} [{r.VerificationStatus}{(r.LatestArtifactName != null ? $", tệp: {r.LatestArtifactName}" : ", chưa nộp")}{(string.IsNullOrWhiteSpace(r.ReviewerFeedback) ? "" : $", phản hồi: {r.ReviewerFeedback}")}]");
            var excerpt = $"Cổng thẩm định {g.Code} ({g.Title}) | Trạng thái: {g.Status} | Điều kiện: {g.ReviewEligibility} | Yêu cầu: " +
                (visibleReqs.Length == 0 ? "Chưa cấu hình" : string.Join("; ", reqSummaries.Take(6)));
            evidence.Add(new("REVIEW_GATE", gateDto.Id, $"{g.Code}: {g.Title}", gateDto.WindowEnd, $"/api/v1/projects/{projectId}/review-gates", excerpt));
        }

        // 6. Project Evidence Ledger (pending or rejected items)
        var rawLedger = await db.ProjectEvidence.AsNoTracking()
            .Where(e => e.ProjectId == projectId && (scope.Whole || (e.MajorId != null && scope.Majors.Contains(e.MajorId.Value))))
            .OrderByDescending(e => e.VerificationStatus == "REJECTED")
            .ThenByDescending(e => e.VerificationStatus == "PENDING")
            .ThenByDescending(e => e.SubmittedAt)
            .Take(15).ToArrayAsync(ct);
        var ledgerItems = rawLedger.Select(e => new ProjectEvidenceLedgerItem(
            $"EV-{e.Id}", e.Id, e.SourceType, e.SourceId,
            e.MajorId.HasValue && majorNameMap.TryGetValue(e.MajorId.Value, out var emn) ? emn : null,
            e.VerificationStatus, Text(e.Notes, 200))).ToArray();
        foreach (var ev in ledgerItems.Where(e => e.VerificationStatus is "REJECTED" or "PENDING").Take(6))
        {
            evidence.Add(new("PROJECT_EVIDENCE", ev.Id, $"Minh chứng {ev.SourceType} #{ev.SourceId}", null,
                $"/api/v1/projects/{projectId}/evidence",
                $"Trạng thái xác minh: {ev.VerificationStatus}{(ev.MajorName != null ? $" | Ngành: {ev.MajorName}" : "")}{(!string.IsNullOrWhiteSpace(ev.Notes) ? $" | Ghi chú: {ev.Notes}" : "")}"));
        }

        // 7. Milestones, Progress Reports, Meetings
        var milestones = new List<MilestoneEvidenceItem>();
        var reports = new List<ProgressReportEvidenceItem>();
        var meetings = new List<MeetingEvidenceItem>();
        if (scope.Whole)
        {
            var ms = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId).OrderBy(m => m.SortOrder).ThenBy(m => m.Id).Take(20)
                .Select(m => new
                {
                    m.Id, m.Title, m.Status, m.DueDate, m.SortOrder, m.Description,
                    Total = m.Tasks.Count(t => t.DeletedAt == null && t.Status != "CANCELLED"),
                    Done = m.Tasks.Count(t => t.DeletedAt == null && (t.Status == "DONE" || t.Status == "COMPLETED"))
                }).ToArrayAsync(ct);
            foreach (var m in ms)
            {
                milestones.Add(new($"MS-{m.Id}", m.Id, Text(m.Title, 180), m.Status, m.DueDate?.ToString("yyyy-MM-dd"), m.SortOrder, Text(m.Description, 240), m.Total, m.Done));
                evidence.Add(new("MILESTONE", $"MS-{m.Id}", Text(m.Title, 180), m.DueDate?.ToString("yyyy-MM-dd"), $"/api/v1/milestones/{m.Id}",
                    $"Trạng thái: {m.Status} | Hoàn thành {m.Done}/{m.Total} công việc{(string.IsNullOrWhiteSpace(m.Description) ? "" : " | " + Text(m.Description, 180))}"));
            }
            var rs = await db.ProgressReports.AsNoTracking().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.Id).Take(5).ToArrayAsync(ct);
            foreach (var r in rs)
            {
                reports.Add(new($"PR-{r.Id}", r.Id, r.ReportType, r.Status, $"{r.PeriodStart:yyyy-MM-dd}/{r.PeriodEnd:yyyy-MM-dd}", Text(r.Summary), Text(r.CompletedWork), Text(r.IssuesAndRisks), Text(r.InProgressWork), Text(r.Blockers), Text(r.Risks), Text(r.NextActions)));
                evidence.Add(new("PROGRESS_REPORT", $"PR-{r.Id}", $"{r.ReportType} ({r.PeriodStart:dd/MM}-{r.PeriodEnd:dd/MM})", $"{r.PeriodStart:yyyy-MM-dd}/{r.PeriodEnd:yyyy-MM-dd}", $"/api/v1/progress-reports/{r.Id}",
                    $"Trạng thái: {r.Status} | {Text(r.Summary, 220)}" + (!string.IsNullOrWhiteSpace(r.Blockers) ? $" | Vướng mắc: {Text(r.Blockers, 160)}" : "")));
            }
            var mt = await db.Meetings.AsNoTracking().Where(m => m.ProjectId == projectId && m.MeetingParticipants.Any(p => p.UserId == scope.Actor))
                .OrderByDescending(m => m.StartAt).ThenByDescending(m => m.Id).Take(5).ToArrayAsync(ct);
            var mtIds = mt.Select(m => m.Id).ToArray();
            var aiInsightsByMeeting = mtIds.Length == 0
                ? new Dictionary<long, string>()
                : (await db.Set<MeetingAiInsight>().AsNoTracking()
                    .Where(i => mtIds.Contains(i.MeetingId) && i.Status == "COMPLETED")
                    .OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
                    .Select(i => new { i.MeetingId, i.InsightType, i.SummaryText })
                    .ToArrayAsync(ct))
                    .GroupBy(i => i.MeetingId)
                    .ToDictionary(
                        g => g.Key,
                        g => Text(g.FirstOrDefault(x => x.InsightType == "FULL_MINUTES")?.SummaryText ?? g.First().SummaryText, 320));

            foreach (var m in mt)
            {
                aiInsightsByMeeting.TryGetValue(m.Id, out var aiSummary);
                var minutesText = Text(m.Minutes ?? aiSummary, 350);
                meetings.Add(new($"MTG-{m.Id}", m.Id, Text(m.Title, 180), m.Status, m.StartAt.ToString("O"), Text(m.Agenda), Text(m.MeetingNotes), minutesText, Text(m.Decisions), Text(m.Blockers)));
                var excerpt = Text(m.Minutes ?? aiSummary ?? m.MeetingNotes ?? m.Agenda, 280);
                if (!string.IsNullOrWhiteSpace(m.Decisions))
                    excerpt = Text($"{excerpt} | Quyết định: {m.Decisions}", 320);
                evidence.Add(new("MEETING", $"MTG-{m.Id}", Text(m.Title, 180), m.StartAt.ToString("O"), $"/api/v1/meetings/{m.Id}", excerpt));
            }

            // Active / latest Sprint summary for Agile context
            var sprints = await db.Set<Sprint>().AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .OrderByDescending(s => s.Status == "ACTIVE")
                .ThenByDescending(s => s.StartAt)
                .Take(2)
                .ToArrayAsync(ct);
            foreach (var sp in sprints)
            {
                var spDesc = $"Sprint ({sp.Status}) | Thời gian: {sp.StartAt:dd/MM/yyyy} – {sp.EndAt:dd/MM/yyyy}";
                evidence.Add(new("SPRINT", $"SPRINT-{sp.Id}", Text(sp.Name, 160), $"{sp.StartAt:yyyy-MM-dd}/{sp.EndAt:yyyy-MM-dd}", $"/api/v1/projects/{projectId}/sprints", spDesc));
            }
        }

        // V4.0 QD-01: Frozen Academic Blueprint Snapshot (if applied to project)
        var blueprintSnapshot = await db.Set<ProjectBlueprintSnapshot>().AsNoTracking()
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.Id)
            .Select(b => new { b.BlueprintId, b.BlueprintVersion, b.AppliedAt, b.SnapshotJson })
            .FirstOrDefaultAsync(ct);
        if (blueprintSnapshot is not null)
        {
            evidence.Add(new("BLUEPRINT", $"BP-{blueprintSnapshot.BlueprintId}",
                $"Academic Blueprint v{blueprintSnapshot.BlueprintVersion}",
                blueprintSnapshot.AppliedAt.ToString("O"),
                $"/api/v1/projects/{projectId}",
                $"Cấu hình Blueprint áp dụng (QĐ-01): {Text(blueprintSnapshot.SnapshotJson, 300)}"));
        }

        var allGateReqs = gateItems.SelectMany(g => g.Requirements).ToArray();
        var stats = new ProjectDocumentProgressStats(
            TotalTasks: totalTasks,
            DoneTasks: doneTasksTotal,
            OverdueTasks: overdueTasksTotal,
            BlockedTasks: blockedTasksTotal,
            ReworkImpactTasks: impactTaskIds.Count,
            TotalDeliverables: deliverableItems.Length,
            AcceptedDeliverables: deliverableItems.Count(d => d.Status is "ACCEPTED" or "APPROVED"),
            RevisionRequiredDeliverables: deliverableItems.Count(d => d.Status == "REVISION_REQUIRED" || d.LatestVersionStatus == "REVISION_REQUIRED"),
            PendingDeliverables: deliverableItems.Count(d => d.LatestVersionNumber == 0 || d.Status is "DRAFT" or "OPEN" or "SUBMITTED"),
            TotalColdItems: coldItems.Count,
            SubmittedColdItems: coldItems.Count(c => c.SubmissionStatus is "SUBMITTED" or "LOCKED"),
            DraftColdItems: coldItems.Count(c => c.SubmissionStatus == "DRAFT"),
            MissingColdItems: coldItems.Count(c => c.SubmissionStatus == "NOT_STARTED"),
            TotalGateRequirements: allGateReqs.Length,
            VerifiedGateRequirements: allGateReqs.Count(r => r.VerificationStatus is "VERIFIED" or "APPROVED" or "ACCEPTED"),
            RejectedGateRequirements: allGateReqs.Count(r => r.VerificationStatus is "REJECTED" or "REVISION_REQUIRED"),
            PendingGateRequirements: allGateReqs.Count(r => r.VerificationStatus is "PENDING" or "SUBMITTED" or "MISSING"));

        var payload = new ProjectEvidencePayload(projectId, scope.Status, totalTasks, taskItems.Length, totalTasks > taskItems.Length,
            milestones, taskItems, reports, meetings, null,
            overview, deliverableItems, coldItems, gateItems, ledgerItems, stats);
        capture.ScopeHash = scope.Hash;
        capture.Evidence = evidence;
        await tx.CommitAsync(ct);
        return new(projectId, scope.Status, evidence, JsonSerializer.Serialize(payload), evidence.Count > 0, evidence.Count, totalTasks, taskItems.Length, totalTasks > taskItems.Length);
    }

    public async Task<ReportBoundedContext> RetrieveReportContextAsync(long projectId, long reportId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var scope = await access.Read(projectId, ct);
        if (!scope.Whole) throw new ForbiddenException("Unclassified project reports are not major-scoped evidence.");
        var r = await db.ProgressReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId && r.ProjectId == projectId, ct) ?? throw new NotFoundException("ProgressReport", reportId);
        if (r.ReportType is not ("WEEKLY" or "MONTHLY")) throw new ArgumentException("Only WEEKLY/MONTHLY reports support AI summary.");
        var period = $"{r.PeriodStart:yyyy-MM-dd}/{r.PeriodEnd:yyyy-MM-dd}";
        var payload = new ReportEvidencePayload(r.Id, projectId, r.ReportType, period, r.Status, Text(r.Summary), Text(r.CompletedWork), Text(r.PlannedWork), Text(r.IssuesAndRisks), [], Text(r.InProgressWork), Text(r.Blockers), Text(r.Risks), Text(r.NextActions));
        EvidenceReferenceDto[] evidence = [new("PROGRESS_REPORT", $"PR-{r.Id}", r.ReportType, period, $"/api/v1/progress-reports/{r.Id}", Text(r.Summary))];
        var enough = new[] { r.Summary, r.CompletedWork, r.PlannedWork, r.IssuesAndRisks, r.InProgressWork, r.Blockers, r.Risks, r.NextActions }.Any(x => !string.IsNullOrWhiteSpace(x));
        capture.ScopeHash = scope.Hash; capture.Evidence = evidence;
        await tx.CommitAsync(ct);
        return new(projectId, r.Id, r.ReportType, r.PeriodStart, r.PeriodEnd, r.Status, Text(r.Summary), Text(r.CompletedWork), Text(r.PlannedWork), Text(r.IssuesAndRisks), evidence, JsonSerializer.Serialize(payload), enough, Text(r.InProgressWork), Text(r.Blockers), Text(r.Risks), Text(r.NextActions));
    }
}
