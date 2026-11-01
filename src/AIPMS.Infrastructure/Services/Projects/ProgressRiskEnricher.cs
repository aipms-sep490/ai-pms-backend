using System.Threading.Tasks;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class ProgressRiskEnricher
{
    internal static async Task<ProjectProgressAnalysisDto> Enrich(AipmsDbContext db, AiActorScope access, AiScope scope,
        ProjectProgressAnalysisDto result, AiEvidenceCapture capture, DateTime now, CancellationToken ct)
    {
        var tasks = await access.Tasks(scope).OrderBy(t => t.Id).Take(2001).Select(t => new {
            t.Id, t.Title, t.Status, t.DueAt, t.UpdatedAt,
            Returns = t.TaskStatusHistories.Count(h => h.OldStatus == "IN_REVIEW" && h.NewStatus == "IN_PROGRESS")
        }).ToArrayAsync(ct);
        var deliverables = await db.Deliverables.AsNoTracking().Where(d => d.ProjectId == scope.ProjectId &&
            (scope.Whole || (d.TargetMajorId.HasValue && scope.Majors.Contains(d.TargetMajorId.Value))))
            .OrderBy(d => d.Id).Take(2001).Select(d => new { d.Id, d.Title, d.DueAt, HasVersion = d.DeliverableVersions.Any() }).ToArrayAsync(ct);
        var sprints = scope.Whole ? await db.Set<Sprint>().AsNoTracking().Where(s => s.ProjectId == scope.ProjectId &&
            (s.Status == "ACTIVE" || s.Status == "PLANNED") && s.EndAt < now).OrderBy(s => s.Id).Take(2001).ToArrayAsync(ct) : [];
        if (tasks.Length > 2000 || deliverables.Length > 2000 || sprints.Length > 2000)
            throw new ArgumentException("AI_ANALYSIS_SOURCE_LIMIT");
        var active = tasks.Where(t => t.Status is not ("DONE" or "CANCELLED")).ToArray();
        var near = deliverables.Where(d => d.DueAt <= now.AddDays(7) && !d.HasVersion).ToArray();
        var lateMilestones = scope.Whole ? await db.Milestones.CountAsync(m => m.ProjectId == scope.ProjectId &&
            m.Status != "COMPLETED" && m.Status != "CANCELLED" && m.DueDate < DateOnly.FromDateTime(now), ct) : 0;
        var missed = scope.Whole ? await db.Meetings.CountAsync(m => m.ProjectId == scope.ProjectId && m.Status == "COMPLETED" &&
            m.StartAt >= now.AddDays(-30) && m.EndAt < now && m.MeetingParticipants.Any(p => p.UserId == scope.Actor && p.AttendanceStatus == "ABSENT"), ct) : 0;
        var evidence = active.Where(t => t.DueAt < now || t.Status == "BLOCKED" || t.UpdatedAt < now.AddDays(-7) || t.Returns >= 2)
            .Select(t => new EvidenceReferenceDto("TASK", $"TASK-{t.Id}", t.Title, null, null, t.Status)).ToList();
        evidence.AddRange(near.Select(d => new EvidenceReferenceDto("DELIVERABLE", $"D-{d.Id}", d.Title, null, null, "Chưa có phiên bản")));
        evidence.AddRange(sprints.Select(s => new EvidenceReferenceDto("SPRINT", $"SPRINT-{s.Id}", s.Name, null, null, "Trễ hạn")));
        capture.Evidence = evidence.Concat(capture.Evidence).DistinctBy(e => e.SourceId).ToArray();
        var updated = ProgressRiskRules.Apply(result, new(tasks.Length, result.ProgressSummary.TotalMilestones ?? 0,
            active.Count(t => t.DueAt < now), active.Count(t => t.Status == "BLOCKED"), lateMilestones + sprints.Length,
            active.Count(t => t.UpdatedAt < now.AddDays(-7)), near.Length, active.Count(t => t.Returns >= 2), missed, active.Count(t => t.DueAt == null)));
        // A mentor's partial evidence must never be presented as the project's overall risk.
        if (!scope.Whole) updated = updated with { DataStatus = "INSUFFICIENT_DATA", RiskLevel = "INSUFFICIENT_DATA",
            RiskScore = null, Confidence = null, Summary = "Chỉ có dữ liệu ngành được phân công; chưa đủ căn cứ dự báo toàn đồ án.",
            Limitations = "Chỉ xem công việc và hạng mục thuộc ngành được phân công. Không suy diễn dữ liệu ngành khác." };
        return updated with { ProjectEvidence = evidence.Take(50).ToArray() };
    }
}
