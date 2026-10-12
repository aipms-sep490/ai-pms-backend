using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class AiRunStore(AipmsDbContext db, AiActorScope access, ICurrentUser current, IAuditTrail audit,
    IAiTextGenerationProvider provider, TimeProvider clock, AiEvidenceCapture capture) : IAiRunStore
{
    public async Task<long> BeginAsync(long projectId, string kind, long? reportId, CancellationToken ct)
    {
        var scope = await access.Read(projectId, ct);
        if (reportId.HasValue && (!scope.Whole || !await db.ProgressReports.AnyAsync(r => r.Id == reportId && r.ProjectId == projectId, ct)))
            throw new NotFoundException("Report", reportId);
        var run = new AiRun { ProjectId = projectId, ActorId = scope.Actor, ReportId = reportId, Kind = kind,
            ScopeHash = scope.Hash, StartedAt = clock.GetUtcNow().UtcDateTime, ModelVersion = provider.ModelVersion,
            ConfigVersion = provider.ConfigurationVersion };
        db.Add(run); await db.SaveChangesAsync(ct); return run.Id;
    }
    public async Task CompleteAsync(long id, ProjectAssistantResponseDto? answer, ReportSummaryDto? report, string? error, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var run = await db.Set<AiRun>().SingleAsync(r => r.Id == id && r.ActorId == current.UserId, ct);
        // A failed completion transaction may have left a newer status in the change tracker.
        await db.Entry(run).ReloadAsync(ct);
        if (run.Status != "RUNNING") throw new ConflictException("AI run already finished.");
        if (error is null)
        {
            var scope = await access.Read(run.ProjectId, ct);
            if (scope.Hash != run.ScopeHash || scope.Hash != capture.ScopeHash || !await EvidenceVisible(scope, capture.Evidence, ct))
                throw new ForbiddenException("AI evidence scope changed during generation.");
        }
        run.Status = error is not null ? "FAILED" : answer?.GenerationStatus ?? report?.GenerationStatus ?? "FAILED";
        run.ErrorCode = error ?? (run.Status == "FALLBACK" ? "AI_PROVIDER_FALLBACK" : run.Status == "INSUFFICIENT_EVIDENCE" ? "AI_INSUFFICIENT_EVIDENCE" : null);
        run.ResultJson = error is not null ? null : answer is not null ? JsonSerializer.Serialize(answer) : JsonSerializer.Serialize(report);
        run.EvidenceJson = error is not null ? "[]" : JsonSerializer.Serialize(capture.Evidence);
        run.CompletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(run.ActorId, "AI_RUN_COMPLETED", "AI_RUN", run.Id,
            new Dictionary<string, object?> { ["projectId"] = run.ProjectId, ["kind"] = run.Kind, ["status"] = run.Status }), ct);
        await tx.CommitAsync(ct);
    }
    public async Task<PagedResult<AiRunDto>> ListAsync(long projectId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue) throw new ArgumentException("Invalid history pagination.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var scope = await access.Read(projectId, ct);
        var q = db.Set<AiRun>().AsNoTracking().Where(r => r.ProjectId == projectId && r.ActorId == scope.Actor && r.ScopeHash == scope.Hash);
        var total = await q.LongCountAsync(ct);
        var rows = await q.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var sources = rows.SelectMany(r => Results(r).Evidence).ToArray();
        var visible = await VisibleEvidence(scope, sources, ct);
        var results = rows.Select(r => Dto(r, visible)).ToArray();
        await tx.CommitAsync(ct); return new(results, page, pageSize, total);
    }
    public async Task<AiRunDto> GetAsync(long projectId, long id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var scope = await access.Read(projectId, ct);
        var row = await db.Set<AiRun>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Id == id && r.ActorId == scope.Actor && r.ScopeHash == scope.Hash, ct)
            ?? throw new NotFoundException("AI run", id);
        var result = Dto(row, await VisibleEvidence(scope, Results(row).Evidence, ct)); await tx.CommitAsync(ct); return result;
    }
    private static (ProjectAssistantResponseDto? Answer, ReportSummaryDto? Report, IReadOnlyList<EvidenceReferenceDto> Evidence) Results(AiRun row)
    {
        var answer = row.ResultJson is not null && row.Kind == "PROJECT_ASK" ? JsonSerializer.Deserialize<ProjectAssistantResponseDto>(row.ResultJson) : null;
        var report = row.ResultJson is not null && row.Kind == "REPORT_SUMMARY" ? JsonSerializer.Deserialize<ReportSummaryDto>(row.ResultJson) : null;
        return (answer, report, JsonSerializer.Deserialize<EvidenceReferenceDto[]>(row.EvidenceJson) ?? []);
    }
    private static AiRunDto Dto(AiRun row, HashSet<string> allowed)
    {
        var (answer, report, evidence) = Results(row);
        var visible = evidence.All(e => allowed.Contains(e.SourceId));
        return new(row.Id, row.ProjectId, row.Kind, row.Status, row.ModelVersion, row.ConfigVersion,
            DateTime.SpecifyKind(row.StartedAt, DateTimeKind.Utc), row.CompletedAt.HasValue ? DateTime.SpecifyKind(row.CompletedAt.Value, DateTimeKind.Utc) : null,
            visible ? row.ErrorCode : "AI_HISTORY_SCOPE_CHANGED", visible ? evidence : [], visible ? answer : null, visible ? report : null);
    }
    private async Task<bool> EvidenceVisible(AiScope scope, IReadOnlyList<EvidenceReferenceDto> evidence, CancellationToken ct)
    {
        var allowed = await VisibleEvidence(scope, evidence, ct);
        return evidence.All(e => allowed.Contains(e.SourceId));
    }
    private async Task<HashSet<string>> VisibleEvidence(AiScope scope, IReadOnlyList<EvidenceReferenceDto> evidence, CancellationToken ct)
    {
        long[] Ids(string type, string prefix) => evidence.Where(e => e.SourceType == type && e.SourceId.StartsWith(prefix, StringComparison.Ordinal))
            .Select(e => long.TryParse(e.SourceId[prefix.Length..], out var id) ? id : 0).Where(id => id > 0).Distinct().ToArray();
        var tasks = Ids("TASK", "TASK-");
        var allowed = (await access.Tasks(scope).Where(t => tasks.Contains(t.Id)).Select(t => t.Id).ToArrayAsync(ct)).Select(id => $"TASK-{id}").ToHashSet();
        if (scope.Whole)
        {
            var milestones = Ids("MILESTONE", "MS-"); var reports = Ids("PROGRESS_REPORT", "PR-"); var meetings = Ids("MEETING", "MTG-");
            allowed.UnionWith((await db.Milestones.Where(m => m.ProjectId == scope.ProjectId && milestones.Contains(m.Id)).Select(m => m.Id).ToArrayAsync(ct)).Select(id => $"MS-{id}"));
            allowed.UnionWith((await db.ProgressReports.Where(r => r.ProjectId == scope.ProjectId && reports.Contains(r.Id)).Select(r => r.Id).ToArrayAsync(ct)).Select(id => $"PR-{id}"));
            allowed.UnionWith((await db.Meetings.Where(m => m.ProjectId == scope.ProjectId && meetings.Contains(m.Id) && m.MeetingParticipants.Any(p => p.UserId == scope.Actor)).Select(m => m.Id).ToArrayAsync(ct)).Select(id => $"MTG-{id}"));
        }
        return allowed;
    }
}
