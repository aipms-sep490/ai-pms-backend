using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiSuite;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.AiSuite;

internal sealed partial class AiSuiteService
{
    private const string FeatureReportDraft = "REPORT_DRAFT";

    public async Task<ProgressReportDraftDto> GenerateProgressReportDraftAsync(long projectId, ProgressReportDraftRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var (project, actor, actorName) = await RequireProjectAccessAsync(projectId, ct);

        var reportType = string.IsNullOrWhiteSpace(request.ReportType) ? "WEEKLY" : request.ReportType.Trim().ToUpperInvariant();
        if (reportType is not ("WEEKLY" or "BIWEEKLY" or "MILESTONE" or "MONTHLY")) reportType = "WEEKLY";

        var todayVn = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(VietnamOffset).DateTime);
        var periodEnd = request.PeriodEnd ?? todayVn;
        var defaultDays = reportType == "MONTHLY" ? 30 : reportType == "BIWEEKLY" ? 14 : 7;
        var periodStart = request.PeriodStart ?? periodEnd.AddDays(-defaultDays);
        if (periodStart > periodEnd) (periodStart, periodEnd) = (periodEnd, periodStart);

        var startUtc = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-7);
        var endUtc = periodEnd.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc).AddHours(-7);

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.Milestone.ProjectId == projectId && t.Status != "CANCELLED")
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Status,
                t.Priority,
                t.CompletedAt,
                t.DueAt,
                t.UpdatedAt,
                MilestoneTitle = t.Milestone.Title,
                Assignees = t.TaskAssignees.Select(a => a.User.FullName).ToArray()
            })
            .ToListAsync(ct);

        var completedTasks = tasks
            .Where(t => t.Status == "DONE" && ((t.CompletedAt ?? t.UpdatedAt) >= startUtc && (t.CompletedAt ?? t.UpdatedAt) <= endUtc))
            .OrderByDescending(t => t.CompletedAt ?? t.UpdatedAt)
            .Take(20)
            .ToList();

        if (completedTasks.Count == 0)
        {
            completedTasks = tasks
                .Where(t => t.Status == "DONE")
                .OrderByDescending(t => t.CompletedAt ?? t.UpdatedAt)
                .Take(8)
                .ToList();
        }

        var inProgressTasks = tasks
            .Where(t => t.Status is "IN_PROGRESS" or "IN_REVIEW" or "TODO")
            .OrderBy(t => t.DueAt ?? DateTime.MaxValue)
            .Take(15)
            .ToList();

        var blockedOrOverdueTasks = tasks
            .Where(t => t.Status == "BLOCKED" || (t.Status != "DONE" && t.DueAt.HasValue && t.DueAt.Value < Now))
            .OrderBy(t => t.DueAt ?? DateTime.MaxValue)
            .Take(12)
            .ToList();

        var meetingInsights = await db.MeetingAiInsights.AsNoTracking()
            .Where(i => i.Meeting.ProjectId == projectId && i.CreatedAt >= startUtc.AddDays(-3) && i.CreatedAt <= endUtc.AddDays(1))
            .OrderByDescending(i => i.CreatedAt)
            .Take(4)
            .Select(i => new { i.Meeting.Title, Summary = Clip(i.SummaryText, 500), i.CreatedAt })
            .ToListAsync(ct);

        var deliverables = await db.Deliverables.AsNoTracking()
            .Where(d => d.Milestone != null && d.Milestone.ProjectId == projectId && d.UpdatedAt >= startUtc && d.UpdatedAt <= endUtc)
            .OrderByDescending(d => d.UpdatedAt)
            .Take(8)
            .Select(d => new { d.Title, d.Status })
            .ToListAsync(ct);

        var evidence = new List<ProgressReportDraftEvidenceDto>();
        foreach (var t in completedTasks.Take(6))
            evidence.Add(new("TASK", t.Title, $"Hoàn thành ({t.MilestoneTitle}) · Phụ trách: {(t.Assignees.Length > 0 ? string.Join(", ", t.Assignees) : "Nhóm")}"));
        foreach (var m in meetingInsights.Take(3))
            evidence.Add(new("MEETING_AI", $"Biên bản AI: {m.Title}", Clip(m.Summary, 180)));
        foreach (var d in deliverables.Take(3))
            evidence.Add(new("DELIVERABLE", d.Title, $"Trạng thái: {d.Status}"));

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_REPORT_DRAFT] Tự động soạn bản nháp Báo cáo Tiến độ Đồ án cho nhóm sinh viên dựa trên Task, Deliverables và Biên bản AI cuộc họp thực tế trong kỳ. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"summary\":string,\"completedWork\":string,\"inProgressWork\":string,\"plannedWork\":string,\"issuesAndRisks\":string}. " +
            "Quy tắc: trình bày rõ ràng theo gạch đầu dòng tiếng Việt, nêu đúng tên task/thành viên có trong dữ liệu, không bịa đặt công việc chưa xảy ra.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            reportType,
            periodStart = periodStart.ToString("yyyy-MM-dd"),
            periodEnd = periodEnd.ToString("yyyy-MM-dd"),
            additionalNote = Clip(request.AdditionalNote, 400),
            completedTasks,
            inProgressTasks,
            blockedOrOverdueTasks,
            meetingAiInsights = meetingInsights,
            deliverables
        }, ct);

        string summary, completedWork, inProgressWork, plannedWork, issuesAndRisks;
        var status = "COMPLETED";
        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            summary = ReadString(r, "summary", $"Báo cáo tiến độ kỳ {periodStart:dd/MM} – {periodEnd:dd/MM} cho đồ án {project.Title}.", 2000);
            completedWork = ReadString(r, "completedWork", "", 3000);
            inProgressWork = ReadString(r, "inProgressWork", "", 2000);
            plannedWork = ReadString(r, "plannedWork", "", 3000);
            issuesAndRisks = ReadString(r, "issuesAndRisks", "", 2500);
        }
        else
        {
            status = "FALLBACK";
            summary = $"Bản nháp tổng hợp kỳ {periodStart:dd/MM/yyyy} – {periodEnd:dd/MM/yyyy}: hoàn thành {completedTasks.Count} task, đang thực hiện {inProgressTasks.Count} task, có {blockedOrOverdueTasks.Count} task cần chú ý.";
            completedWork = completedTasks.Count > 0 ? string.Join("\n", completedTasks.Select(t => $"- {t.Title}")) : "- Đang cập nhật tiến độ công việc.";
            inProgressWork = inProgressTasks.Count > 0 ? string.Join("\n", inProgressTasks.Take(8).Select(t => $"- {t.Title} ({t.Status})")) : "- Chưa có task đang thực hiện.";
            plannedWork = inProgressWork;
            issuesAndRisks = blockedOrOverdueTasks.Count > 0 ? string.Join("\n", blockedOrOverdueTasks.Select(t => $"- {t.Title} ({t.Status})")) : "- Không ghi nhận rủi ro lớn.";
        }

        var draftPayload = new { summary, completedWork, inProgressWork, plannedWork, issuesAndRisks };
        var meta = await SaveSnapshotAsync(FeatureReportDraft, "PROJECT", projectId, projectId, null, status, $"Nháp báo cáo {reportType} ({periodStart:dd/MM}–{periodEnd:dd/MM})", draftPayload, actor, actorName, ct);

        return new ProgressReportDraftDto(
            projectId, reportType, periodStart, periodEnd,
            summary, completedWork, inProgressWork, plannedWork, issuesAndRisks,
            evidence, meta);
    }
}
