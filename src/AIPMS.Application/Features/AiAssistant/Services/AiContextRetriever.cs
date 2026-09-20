using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;

namespace AIPMS.Application.Features.AiAssistant.Services;

public sealed class AiContextRetriever(
    IProjectAccessService projectAccess,
    IProjectProgressDataReader dataReader,
    IProgressReportRepository reportRepository,
    IMilestoneRepository milestoneRepository,
    ITaskRepository taskRepository,
    IMeetingRepository meetingRepository,
    IContributionRepository contributionRepository,
    ICurrentUser currentUser) : IAiContextRetriever
{
    private const int MaxTasks = 15;
    private const int MaxMilestones = 10;
    private const int MaxReports = 5;
    private const int MaxMeetings = 5;
    private const int MaxStringLength = 350;

    public async Task<ProjectBoundedContext> RetrieveProjectContextAsync(
        long projectId,
        string query,
        CancellationToken cancellationToken = default)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");

        if (!await dataReader.ProjectExistsAsync(projectId, cancellationToken))
        {
            throw new NotFoundException("Project", projectId);
        }

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        var facts = await dataReader.GetProjectProgressFactsAsync(projectId, cancellationToken);
        var projectStatus = facts?.ProjectStatus ?? "UNKNOWN";

        var evidenceList = new List<EvidenceReferenceDto>();

        var evidenceBuilder = new StringBuilder();
        evidenceBuilder.AppendLine($"<project_evidence project_id=\"{projectId}\" status=\"{projectStatus}\">");

        // 1. Milestones
        var milestones = await milestoneRepository.GetProjectMilestonesAsync(projectId, cancellationToken);
        if (milestones.Count > 0)
        {
            evidenceBuilder.AppendLine("  <milestones>");
            foreach (var m in milestones.Take(MaxMilestones))
            {
                var dateStr = m.DueDate?.ToString("yyyy-MM-dd") ?? m.StartDate?.ToString("yyyy-MM-dd");
                var excerpt = $"Status: {m.Status}, SortOrder: {m.SortOrder}" + (m.Description is not null ? $", Desc: {Sanitize(m.Description)}" : "");
                var refDto = new EvidenceReferenceDto(
                    SourceType: "MILESTONE",
                    SourceId: $"MS-{m.Id}",
                    Title: Sanitize(m.Title),
                    PeriodOrDate: dateStr,
                    ReferenceUrl: $"/api/v1/milestones/{m.Id}",
                    Excerpt: excerpt);
                evidenceList.Add(refDto);
                evidenceBuilder.AppendLine($"    <milestone id=\"MS-{m.Id}\" title=\"{Sanitize(m.Title)}\" status=\"{m.Status}\" due=\"{dateStr}\">{excerpt}</milestone>");
            }
            evidenceBuilder.AppendLine("  </milestones>");
        }

        // 2. Tasks
        var taskResult = await taskRepository.GetTasksAsync(
            projectId: projectId,
            milestoneId: null,
            status: null,
            priority: null,
            assigneeUserId: null,
            search: null,
            dueFrom: null,
            dueTo: null,
            isOverdue: null,
            isBlocked: null,
            page: 1,
            pageSize: MaxTasks,
            cancellationToken: cancellationToken);

        var allTasks = new List<TaskDto>(taskResult.Items);

        // For blocker/risk queries, ensure blocked tasks beyond first page are retrieved (P1 #3)
        var isBlockerQuery = query.Contains("block", StringComparison.OrdinalIgnoreCase) ||
                             query.Contains("risk", StringComparison.OrdinalIgnoreCase) ||
                             query.Contains("issue", StringComparison.OrdinalIgnoreCase);

        if (isBlockerQuery)
        {
            try
            {
                var blockedResult = await taskRepository.GetTasksAsync(
                    projectId: projectId,
                    milestoneId: null,
                    status: null,
                    priority: null,
                    assigneeUserId: null,
                    search: null,
                    dueFrom: null,
                    dueTo: null,
                    isOverdue: null,
                    isBlocked: true,
                    page: 1,
                    pageSize: 15,
                    cancellationToken: cancellationToken);

                foreach (var bt in blockedResult.Items)
                {
                    if (!allTasks.Any(t => t.Id == bt.Id))
                    {
                        allTasks.Add(bt);
                    }
                }
            }
            catch
            {
                // Non-fatal if blocked query fails
            }
        }

        var totalTasks = (int)Math.Max(taskResult.TotalCount, (long)allTasks.Count);
        var retrievedTasks = allTasks.Count;
        var tasksTruncated = totalTasks > retrievedTasks;

        if (allTasks.Count > 0)
        {
            var now = DateTime.UtcNow;
            evidenceBuilder.AppendLine($"  <tasks total_count=\"{totalTasks}\" retrieved_count=\"{retrievedTasks}\" truncated=\"{tasksTruncated.ToString().ToLowerInvariant()}\">");
            foreach (var t in allTasks)
            {
                var isOverdue = t.DueAt.HasValue && t.DueAt.Value < now && !string.Equals(t.Status, "DONE", StringComparison.OrdinalIgnoreCase);
                var isBlocked = string.Equals(t.Status, "BLOCKED", StringComparison.OrdinalIgnoreCase);
                var dueStr = t.DueAt?.ToString("yyyy-MM-dd HH:mm");
                var excerpt = $"Status: {t.Status}, Priority: {t.Priority ?? "Normal"}, Overdue: {isOverdue}, Blocked: {isBlocked}" +
                              (t.Description is not null ? $", Desc: {Sanitize(t.Description)}" : "");
                var refDto = new EvidenceReferenceDto(
                    SourceType: "TASK",
                    SourceId: $"TASK-{t.Id}",
                    Title: Sanitize(t.Title),
                    PeriodOrDate: dueStr,
                    ReferenceUrl: $"/api/v1/tasks/{t.Id}",
                    Excerpt: excerpt);
                evidenceList.Add(refDto);
                evidenceBuilder.AppendLine($"    <task id=\"TASK-{t.Id}\" title=\"{Sanitize(t.Title)}\" status=\"{t.Status}\" priority=\"{t.Priority}\" due=\"{dueStr}\">{excerpt}</task>");
            }
            evidenceBuilder.AppendLine("  </tasks>");
        }

        // 3. Progress Reports
        var reportResult = await reportRepository.GetReportsAsync(
            projectId: projectId,
            reportType: null,
            status: null,
            from: null,
            to: null,
            page: 1,
            pageSize: MaxReports,
            cancellationToken: cancellationToken);

        if (reportResult.Items.Count > 0)
        {
            evidenceBuilder.AppendLine("  <progress_reports>");
            foreach (var r in reportResult.Items)
            {
                var periodStr = $"{r.PeriodStart:yyyy-MM-dd} to {r.PeriodEnd:yyyy-MM-dd}";
                var excerpt = $"Type: {r.ReportType}, Status: {r.Status}, Summary: {Sanitize(r.Summary)}" +
                              (!string.IsNullOrWhiteSpace(r.CompletedWork) ? $", Completed: {Sanitize(r.CompletedWork)}" : "") +
                              (!string.IsNullOrWhiteSpace(r.IssuesAndRisks) ? $", Issues/Risks: {Sanitize(r.IssuesAndRisks)}" : "");
                var refDto = new EvidenceReferenceDto(
                    SourceType: "PROGRESS_REPORT",
                    SourceId: $"PR-{r.Id}",
                    Title: $"{r.ReportType} Report ({periodStr})",
                    PeriodOrDate: periodStr,
                    ReferenceUrl: $"/api/v1/progress-reports/{r.Id}",
                    Excerpt: excerpt);
                evidenceList.Add(refDto);
                evidenceBuilder.AppendLine($"    <report id=\"PR-{r.Id}\" type=\"{r.ReportType}\" period=\"{periodStr}\" status=\"{r.Status}\">{excerpt}</report>");
            }
            evidenceBuilder.AppendLine("  </progress_reports>");
        }

        // 4. Meetings
        var meetingResult = await meetingRepository.GetMeetingsAsync(
            projectId: projectId,
            status: null,
            from: null,
            to: null,
            page: 1,
            pageSize: MaxMeetings,
            cancellationToken: cancellationToken);

        if (meetingResult.Items.Count > 0)
        {
            evidenceBuilder.AppendLine("  <meetings>");
            foreach (var m in meetingResult.Items)
            {
                var dateStr = m.StartAt.ToString("yyyy-MM-dd HH:mm");
                var excerpt = $"Status: {m.Status}" +
                              (!string.IsNullOrWhiteSpace(m.Agenda) ? $", Agenda: {Sanitize(m.Agenda)}" : "") +
                              (!string.IsNullOrWhiteSpace(m.MeetingNotes) ? $", Notes: {Sanitize(m.MeetingNotes)}" : "");
                var refDto = new EvidenceReferenceDto(
                    SourceType: "MEETING",
                    SourceId: $"MTG-{m.Id}",
                    Title: Sanitize(m.Title),
                    PeriodOrDate: dateStr,
                    ReferenceUrl: $"/api/v1/meetings/{m.Id}",
                    Excerpt: excerpt);
                evidenceList.Add(refDto);
                evidenceBuilder.AppendLine($"    <meeting id=\"MTG-{m.Id}\" title=\"{Sanitize(m.Title)}\" status=\"{m.Status}\" start=\"{dateStr}\">{excerpt}</meeting>");
            }
            evidenceBuilder.AppendLine("  </meetings>");
        }

        // 5. Contribution summary
        try
        {
            var contributionSummary = await contributionRepository.GetSummaryAsync(projectId, storedOnly: true, cancellationToken);
            if (contributionSummary != null && contributionSummary.Members.Count > 0)
            {
                var totalTasksCompleted = contributionSummary.Members.Sum(m => m.CompletedTasks);
                var excerpt = $"TeamMembers: {contributionSummary.Members.Count}, TotalTasksCompleted: {totalTasksCompleted}";
                var dateStr = contributionSummary.SnapshotAt?.ToString("yyyy-MM-dd") ?? "N/A";
                var refDto = new EvidenceReferenceDto(
                    SourceType: "CONTRIBUTION",
                    SourceId: $"CONTRIB-{projectId}",
                    Title: "Project Contribution Summary",
                    PeriodOrDate: dateStr,
                    ReferenceUrl: $"/api/v1/projects/{projectId}/contributions",
                    Excerpt: excerpt);
                evidenceList.Add(refDto);
                evidenceBuilder.AppendLine($"  <contributions snapshot=\"{dateStr}\">{excerpt}</contributions>");
            }
        }
        catch
        {
            // Contribution data is optional evidence; non-fatal if absent
        }

        evidenceBuilder.AppendLine("</project_evidence>");

        var hasSufficientEvidence = evidenceList.Count > 0;
        if (hasSufficientEvidence && facts != null)
        {
            evidenceList.Insert(0, new EvidenceReferenceDto(
                SourceType: "PROJECT",
                SourceId: $"PROJ-{projectId}",
                Title: $"Project #{projectId}",
                PeriodOrDate: null,
                ReferenceUrl: $"/api/v1/projects/{projectId}",
                Excerpt: $"Status: {projectStatus}, TeamMembers: {facts.TeamMemberCount}"));
        }

        return new ProjectBoundedContext(
            ProjectId: projectId,
            ProjectStatus: projectStatus,
            EvidenceList: evidenceList,
            FormattedEvidenceText: evidenceBuilder.ToString(),
            HasSufficientEvidence: hasSufficientEvidence,
            TotalEvidenceCount: evidenceList.Count,
            TotalTasks: totalTasks,
            RetrievedTasks: retrievedTasks,
            TasksTruncated: tasksTruncated);
    }

    public async Task<ReportBoundedContext> RetrieveReportContextAsync(
        long projectId,
        long reportId,
        CancellationToken cancellationToken = default)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");

        if (!await dataReader.ProjectExistsAsync(projectId, cancellationToken))
        {
            throw new NotFoundException("Project", projectId);
        }

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        var report = await reportRepository.GetDetailByIdAsync(reportId, cancellationToken)
            ?? throw new NotFoundException("ProgressReport", reportId);

        if (report.ProjectId != projectId)
        {
            throw new NotFoundException("ProgressReport", reportId);
        }

        if (!string.Equals(report.ReportType, "WEEKLY", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(report.ReportType, "MONTHLY", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["reportType"] = ["Only WEEKLY and MONTHLY reports are supported for summarization."]
            });
        }

        var evidenceList = new List<EvidenceReferenceDto>();
        var periodStr = $"{report.PeriodStart:yyyy-MM-dd} to {report.PeriodEnd:yyyy-MM-dd}";

        var primaryRef = new EvidenceReferenceDto(
            SourceType: "PROGRESS_REPORT",
            SourceId: $"PR-{report.Id}",
            Title: $"{report.ReportType} Report ({periodStr})",
            PeriodOrDate: periodStr,
            ReferenceUrl: $"/api/v1/progress-reports/{report.Id}",
            Excerpt: Sanitize(report.Summary));
        evidenceList.Add(primaryRef);

        if (report.Feedbacks != null && report.Feedbacks.Count > 0)
        {
            foreach (var fb in report.Feedbacks)
            {
                var fbRef = new EvidenceReferenceDto(
                    SourceType: "SUPERVISOR_FEEDBACK",
                    SourceId: $"FB-{fb.Id}",
                    Title: $"Feedback from {Sanitize(fb.SupervisorName)}",
                    PeriodOrDate: fb.CreatedAt.ToString("yyyy-MM-dd"),
                    ReferenceUrl: $"/api/v1/progress-reports/{report.Id}",
                    Excerpt: Sanitize(fb.FeedbackText));
                evidenceList.Add(fbRef);
            }
        }

        var evidenceBuilder = new StringBuilder();
        evidenceBuilder.AppendLine($"<report_evidence report_id=\"{report.Id}\" project_id=\"{projectId}\" type=\"{report.ReportType}\" period=\"{periodStr}\" status=\"{report.Status}\">");
        evidenceBuilder.AppendLine($"  <summary>{Sanitize(report.Summary)}</summary>");
        if (!string.IsNullOrWhiteSpace(report.CompletedWork))
            evidenceBuilder.AppendLine($"  <completed_work>{Sanitize(report.CompletedWork)}</completed_work>");
        if (!string.IsNullOrWhiteSpace(report.PlannedWork))
            evidenceBuilder.AppendLine($"  <planned_work>{Sanitize(report.PlannedWork)}</planned_work>");
        if (!string.IsNullOrWhiteSpace(report.IssuesAndRisks))
            evidenceBuilder.AppendLine($"  <issues_and_risks>{Sanitize(report.IssuesAndRisks)}</issues_and_risks>");

        if (report.Feedbacks != null && report.Feedbacks.Count > 0)
        {
            evidenceBuilder.AppendLine("  <feedbacks>");
            foreach (var fb in report.Feedbacks)
            {
                evidenceBuilder.AppendLine($"    <feedback by=\"{Sanitize(fb.SupervisorName)}\" date=\"{fb.CreatedAt:yyyy-MM-dd}\">{Sanitize(fb.FeedbackText)}</feedback>");
            }
            evidenceBuilder.AppendLine("  </feedbacks>");
        }
        evidenceBuilder.AppendLine("</report_evidence>");

        var hasSufficientEvidence = !string.IsNullOrWhiteSpace(report.Summary) ||
                                     !string.IsNullOrWhiteSpace(report.CompletedWork) ||
                                     !string.IsNullOrWhiteSpace(report.PlannedWork) ||
                                     !string.IsNullOrWhiteSpace(report.IssuesAndRisks);

        return new ReportBoundedContext(
            ProjectId: projectId,
            ReportId: report.Id,
            ReportType: report.ReportType,
            PeriodStart: report.PeriodStart,
            PeriodEnd: report.PeriodEnd,
            Status: report.Status,
            Summary: report.Summary,
            CompletedWork: report.CompletedWork,
            PlannedWork: report.PlannedWork,
            IssuesAndRisks: report.IssuesAndRisks,
            EvidenceList: evidenceList,
            FormattedEvidenceText: evidenceBuilder.ToString(),
            HasSufficientEvidence: hasSufficientEvidence);
    }

    private static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sanitized = text
            .Replace("</project_evidence>", "[sanitized_tag]")
            .Replace("<project_evidence>", "[sanitized_tag]")
            .Replace("</report_evidence>", "[sanitized_tag]")
            .Replace("<report_evidence>", "[sanitized_tag]")
            .Replace("<system>", "[sanitized_tag]")
            .Replace("</system>", "[sanitized_tag]");

        return sanitized.Length > MaxStringLength ? sanitized[..MaxStringLength] + "..." : sanitized;
    }
}
