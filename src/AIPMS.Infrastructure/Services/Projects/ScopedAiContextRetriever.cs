using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class AiEvidenceCapture
{
    public string? ScopeHash { get; set; }
    public IReadOnlyList<EvidenceReferenceDto> Evidence { get; set; } = [];
}

internal sealed class ScopedAiContextRetriever(AipmsDbContext db, AiActorScope access, TimeProvider clock, AiEvidenceCapture capture) : IAiContextRetriever
{
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Length > 350 ? value[..350] + "..." : value;
    public async Task<ProjectBoundedContext> RetrieveProjectContextAsync(long projectId, string query, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var scope = await access.Read(projectId, ct); var evidence = new List<EvidenceReferenceDto>();
        var tasksQuery = access.Tasks(scope); var total = await tasksQuery.CountAsync(ct);
        var tasks = await tasksQuery.OrderByDescending(t => t.Status == "BLOCKED").ThenBy(t => t.Id).Take(30)
            .Select(t => new { t.Id, t.Title, t.Status, t.Priority, t.DueAt, t.Description }).ToArrayAsync(ct);
        var taskItems = tasks.Select(t => new TaskEvidenceItem($"TASK-{t.Id}", t.Id, Text(t.Title), t.Status, t.Priority,
            t.DueAt?.ToString("O"), t.DueAt < clock.GetUtcNow().UtcDateTime && t.Status is not ("DONE" or "CANCELLED"), t.Status == "BLOCKED", Text(t.Description))).ToArray();
        foreach (var t in taskItems) evidence.Add(new("TASK", t.Id, t.Title, t.DueAt, $"/api/v1/tasks/{t.TaskId}", $"Status: {t.Status}, {t.Description}"));
        var milestones = new List<MilestoneEvidenceItem>(); var reports = new List<ProgressReportEvidenceItem>(); var meetings = new List<MeetingEvidenceItem>();
        if (scope.Whole)
        {
            var ms = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId).OrderBy(m => m.SortOrder).ThenBy(m => m.Id).Take(10)
                .Select(m => new { m.Id, m.Title, m.Status, m.DueDate, m.SortOrder, m.Description }).ToArrayAsync(ct);
            foreach (var m in ms) { milestones.Add(new($"MS-{m.Id}", m.Id, Text(m.Title), m.Status, m.DueDate?.ToString("yyyy-MM-dd"), m.SortOrder, Text(m.Description)));
                evidence.Add(new("MILESTONE", $"MS-{m.Id}", Text(m.Title), m.DueDate?.ToString("yyyy-MM-dd"), $"/api/v1/milestones/{m.Id}", m.Status)); }
            var rs = await db.ProgressReports.AsNoTracking().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.Id).Take(5).ToArrayAsync(ct);
            foreach (var r in rs) { reports.Add(new($"PR-{r.Id}", r.Id, r.ReportType, r.Status, $"{r.PeriodStart:yyyy-MM-dd}/{r.PeriodEnd:yyyy-MM-dd}", Text(r.Summary), Text(r.CompletedWork), Text(r.IssuesAndRisks), Text(r.InProgressWork), Text(r.Blockers), Text(r.Risks), Text(r.NextActions)));
                evidence.Add(new("PROGRESS_REPORT", $"PR-{r.Id}", r.ReportType, null, $"/api/v1/progress-reports/{r.Id}", Text(r.Summary))); }
            var mt = await db.Meetings.AsNoTracking().Where(m => m.ProjectId == projectId && m.MeetingParticipants.Any(p => p.UserId == scope.Actor))
                .OrderByDescending(m => m.StartAt).ThenByDescending(m => m.Id).Take(5).ToArrayAsync(ct);
            foreach (var m in mt) { meetings.Add(new($"MTG-{m.Id}", m.Id, Text(m.Title), m.Status, m.StartAt.ToString("O"), Text(m.Agenda), Text(m.MeetingNotes), Text(m.Minutes), Text(m.Decisions), Text(m.Blockers)));
                evidence.Add(new("MEETING", $"MTG-{m.Id}", Text(m.Title), m.StartAt.ToString("O"), $"/api/v1/meetings/{m.Id}", Text(m.Minutes ?? m.MeetingNotes))); }
        }
        var payload = new ProjectEvidencePayload(projectId, scope.Status, total, tasks.Length, total > tasks.Length, milestones, taskItems, reports, meetings, null);
        capture.ScopeHash = scope.Hash; capture.Evidence = evidence;
        await tx.CommitAsync(ct);
        return new(projectId, scope.Status, evidence, JsonSerializer.Serialize(payload), evidence.Count > 0, evidence.Count, total, tasks.Length, total > tasks.Length);
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
