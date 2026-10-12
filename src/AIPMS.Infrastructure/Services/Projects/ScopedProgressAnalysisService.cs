using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ScopedProgressAnalysisService(AipmsDbContext db, AiActorScope access,
    IProgressAnalysisService calculator, IAiRunStore runs, AiEvidenceCapture capture, TimeProvider clock)
    : IScopedProgressAnalysisService
{
    private const int SourceLimit = 2000;

    public async Task<ProjectProgressAnalysisDto> AnalyzeAsync(long projectId, bool record, CancellationToken ct)
    {
        if (projectId <= 0) throw new ArgumentException("Project ID must be positive.");
        long? runId = record ? await runs.BeginAsync(projectId, "PROGRESS_ANALYSIS", null, ct) : null;
        try
        {
            ProjectProgressAnalysisDto result;
            await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
            {
                var scope = await access.Read(projectId, ct);
                var now = clock.GetUtcNow().UtcDateTime;
                var tasks = await access.Tasks(scope).OrderBy(t => t.Id).Take(SourceLimit + 1)
                    .Select(t => new TaskFact(t.Id, t.MilestoneId, "", t.Status, t.Priority,
                        t.StartAt, t.DueAt, t.CompletedAt, t.TaskAssignees.Count)).ToArrayAsync(ct);
                MilestoneFact[] milestones = [];
                ProgressReportFact[] reports = [];
                MeetingFact[] meetings = [];
                if (scope.Whole)
                {
                    milestones = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId)
                        .OrderBy(m => m.Id).Take(SourceLimit + 1)
                        .Select(m => new MilestoneFact(m.Id, "", m.Status, m.StartDate, m.DueDate, m.SortOrder)).ToArrayAsync(ct);
                    reports = await db.ProgressReports.AsNoTracking().Where(r => r.ProjectId == projectId)
                        .OrderBy(r => r.Id).Take(SourceLimit + 1)
                        .Select(r => new ProgressReportFact(r.Id, r.ReportType, r.PeriodStart, r.PeriodEnd, r.Status, r.SubmittedAt)).ToArrayAsync(ct);
                    meetings = await db.Meetings.AsNoTracking().Where(m => m.ProjectId == projectId
                            && m.MeetingParticipants.Any(p => p.UserId == scope.Actor))
                        .OrderBy(m => m.Id).Take(SourceLimit + 1)
                        .Select(m => new MeetingFact(m.Id, "", m.Status, m.StartAt, m.EndAt)).ToArrayAsync(ct);
                }
                if (new[] { tasks.Length, milestones.Length, reports.Length, meetings.Length }.Any(n => n > SourceLimit))
                    throw new ArgumentException("AI_ANALYSIS_SOURCE_LIMIT: analysis supports at most 2000 visible records per source type.");

                var evidence = new List<EvidenceReferenceDto>();
                evidence.AddRange(tasks.Select(t => Reference("TASK", $"TASK-{t.Id}")));
                evidence.AddRange(milestones.Select(m => Reference("MILESTONE", $"MS-{m.Id}")));
                evidence.AddRange(reports.Select(r => Reference("PROGRESS_REPORT", $"PR-{r.Id}")));
                evidence.AddRange(meetings.Select(m => Reference("MEETING", $"MTG-{m.Id}")));
                capture.ScopeHash = scope.Hash;
                capture.Evidence = evidence;
                // This calculator needs no team identity/roster; do not load unneeded personal context.
                result = calculator.Analyze(new(projectId, scope.Status, 0, 0, milestones, tasks, reports, meetings), now, ct);
                if (!scope.Whole)
                {
                    // Project-level model features cannot be estimated from a mentor's partial task set.
                    var active = tasks.Where(t => t.Status is "TODO" or "IN_PROGRESS" or "BLOCKED" or "IN_REVIEW").ToArray();
                    result = result with
                    {
                        DataStatus = "INSUFFICIENT_DATA", RiskLevel = "INSUFFICIENT_DATA", RiskScore = null, Confidence = null,
                        ContextScope = "MAJOR", MajorIds = scope.Majors.Order().ToArray(),
                        ProgressSummary = result.ProgressSummary with { TotalMilestones = null, CompletedMilestones = null,
                            OverdueTasks = active.Count(t => t.DueAt < now) },
                        FeatureSnapshot = new(null, null, null, null, null, null, null, null, null, null, null),
                        Factors = [], Recommendations = ["Review the visible major task counts; project-wide risk is unavailable in this scope."],
                        UnavailableMetrics = ["PROJECT_RISK", "MILESTONES", "REPORTS", "MEETINGS", "CONTRIBUTIONS"],
                        Limitations = "Only tasks classified in the current actor's majors are included. Missing project metrics are not zero."
                    };
                }
                else
                {
                    result = result with { ContextScope = "PROJECT_WITH_INVITED_MEETINGS",
                        Limitations = (result.Limitations ?? "") + " Meeting metrics include only meetings the actor is invited to." };
                }
                result = result with { RunId = runId };
                await tx.CommitAsync(ct);
            }
            if (runId.HasValue) await runs.CompleteProgressAsync(runId.Value, result, ct);
            return result;
        }
        catch
        {
            if (runId.HasValue)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await runs.CompleteAsync(runId.Value, null, null, "AI_ANALYSIS_INTERRUPTED", cleanup.Token); }
                catch { /* A failed audit leaves an explicit RUNNING reservation, without a result. */ }
            }
            throw;
        }
    }

    private static EvidenceReferenceDto Reference(string type, string id) => new(type, id, type, null, null, null);
}
