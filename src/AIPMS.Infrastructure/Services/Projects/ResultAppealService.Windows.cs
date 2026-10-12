using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Appeals;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.Services;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ResultAppealService
{
    private async Task<ResultAppealPolicy> Approved(long projectId, long id, CancellationToken ct) =>
        await db.Set<ResultAppealPolicy>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.ProjectId == projectId, ct)
        ?? throw new ConflictException("A recorded, versioned academic appeal policy is required.", "APPEAL_POLICY_REQUIRED");
    private static AppealPolicyDto PolicyDto(ResultAppealPolicy p) => new(p.Id, p.ProjectId, p.Version, p.ApprovalReference, p.RecordedBy, Utc(p.RecordedAt));
    public async Task<AppealPolicyDto?> Policy(long projectId, CancellationToken ct)
    {
        var access = await Scope(projectId, ct);
        if (!access.Student && !access.Staff) throw new ForbiddenException();
        var row = await db.Set<ResultAppealPolicy>().AsNoTracking().Where(p => p.ProjectId == projectId).OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct);
        return row is null ? null : PolicyDto(row);
    }
    public Task<AppealPolicyDto> RecordPolicy(long projectId, RecordAppealPolicyRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); Lead(await Scope(projectId, ct));
        if (string.IsNullOrWhiteSpace(input.ApprovalReference) || input.ApprovalReference.Length > 1000 || input.ExpectedVersion < 0)
            throw Invalid("An actual academic approval source <=1000 characters and expected version are required; technical demo is not approval.");
        var latest = await db.Set<ResultAppealPolicy>().Where(p => p.ProjectId == projectId).Select(p => (int?)p.Version).MaxAsync(ct) ?? 0;
        if (latest != input.ExpectedVersion) throw new ConflictException("Appeal policy changed.", "APPEAL_STALE");
        var row = new ResultAppealPolicy { ProjectId = projectId, Version = latest+1, ApprovalReference = input.ApprovalReference.Trim(), RecordedBy = Actor, RecordedAt = Now };
        db.Add(row); await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "APPEAL_POLICY_RECORDED", "RESULT_APPEAL_POLICY", row.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["version"] = row.Version }), ct);
        return PolicyDto(row);
    }, ct);
    private static long Key(string type, long? studentId)
    {
        if (type == "PROJECT" && studentId is null) return 0;
        if (type == "STUDENT" && studentId is > 0) return studentId.Value;
        throw Invalid("Explicit PROJECT without studentId or STUDENT with positive studentId required.");
    }
    private AppealWindowDto WindowDto(ResultAppealWindow row, Access access)
    {
        var targets = JsonSerializer.Deserialize<AppealWindowTarget[]>(row.TargetsJson)!;
        if (!access.Lead) targets = targets.Where(t => access.Student && t.StudentId == Actor).ToArray();
        return new(row.Id, row.PolicyId, row.Status == "CLOSED" ? "CLOSED" : Now < row.OpensAt ? "SCHEDULED" : Now >= row.ClosesAt ? "EXPIRED" : "OPEN",
            Utc(row.OpensAt), Utc(row.ClosesAt), row.ConcurrencyToken.ToString("N"), targets);
    }
    public Task<AppealWindowDto> OpenWindow(long projectId, OpenAppealWindowRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); Lead(access);
        await Approved(projectId, input.PolicyId, ct);
        var opens = Seconds(input.OpensAt); var closes = Seconds(input.ClosesAt);
        if (opens >= closes || closes <= Now || closes-opens > TimeSpan.FromDays(366) || input.Targets is null || input.Targets.Count is < 1 or > 1000
            || input.Targets.Any(t => t is null || t.ResultVersion < 1)) throw Invalid("Bounded future window and 1..1000 explicit published targets required.");
        var current = await revisions.Current(projectId, ct);
        if (input.Targets.Select(t => Key(t.TargetType, t.StudentId)).Distinct().Count() != input.Targets.Count) throw Invalid("Targets must be unique.");
        foreach (var target in input.Targets)
        {
            var published = current.SingleOrDefault(t => t.Key == Key(target.TargetType, target.StudentId));
            if (published is null || published.Version != target.ResultVersion) throw new ConflictException("Target result/version is not current.", "RESULT_REVISION_STALE");
            ResultRevisionCalculator.Calculate(published.Inputs, []);
        }
        var overlapping = await db.Set<ResultAppealWindow>().Where(w => w.ProjectId == projectId && w.Status == "OPEN"
            && w.OpensAt < closes && w.ClosesAt > opens).OrderBy(w => w.Id).Take(1001).ToArrayAsync(ct);
        if (overlapping.Length > 1000) throw new ConflictException("Close unused windows before opening another.");
        if (overlapping.Any(w => JsonSerializer.Deserialize<AppealWindowTarget[]>(w.TargetsJson)!.Any(t => input.Targets.Contains(t))))
            throw new ConflictException("An overlapping window already covers this result version.", "APPEAL_WINDOW_EXISTS");
        var row = new ResultAppealWindow { ProjectId = projectId, PolicyId = input.PolicyId, OpensAt = opens, ClosesAt = closes,
            TargetsJson = JsonSerializer.Serialize(input.Targets), CreatedBy = Actor };
        db.Add(row); await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "APPEAL_WINDOW_OPENED", "RESULT_APPEAL_WINDOW", row.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["policyId"] = input.PolicyId, ["targetCount"] = input.Targets.Count }), ct);
        return WindowDto(row, access);
    }, ct);
    public async Task<PagedResult<AppealWindowDto>> Windows(long projectId, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); var access = await Scope(projectId, ct);
        if (!access.Lead && !access.Student) throw new ForbiddenException();
        var query = db.Set<ResultAppealWindow>().AsNoTracking().Where(w => w.ProjectId == projectId);
        // JSON target selection is performed in SQL before count/paging.
        if (!access.Lead) query = db.Set<ResultAppealWindow>().FromSqlInterpolated($"SELECT w.* FROM dbo.result_appeal_windows w WHERE w.project_id={projectId} AND EXISTS(SELECT 1 FROM OPENJSON(w.targets_json) WITH (StudentId bigint '$.StudentId') t WHERE t.StudentId={Actor})").AsNoTracking();
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(w => w.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(w => WindowDto(w, access)).ToArray(), page, pageSize, count);
    }
    public Task<AppealWindowDto> CloseWindow(long projectId, long id, string token, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); Lead(access);
        var row = await db.Set<ResultAppealWindow>().SingleOrDefaultAsync(w => w.ProjectId == projectId && w.Id == id, ct) ?? throw new NotFoundException("AppealWindow", id);
        Token(row.ConcurrencyToken, token); if (row.Status != "OPEN") throw new ConflictException("Window already closed.");
        row.Status = "CLOSED"; row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "APPEAL_WINDOW_CLOSED", "RESULT_APPEAL_WINDOW", id,
            new Dictionary<string, object?> { ["projectId"] = projectId }), ct);
        return WindowDto(row, access);
    }, ct);

    public Task<CorrectionRequestDto> Create(long projectId, CreateCorrectionRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct);
        await Approved(projectId, input.PolicyId, ct); var reason = Reason(input.Reason); var key = Key(input.TargetType, input.StudentId);
        if (input.Source == "STUDENT_APPEAL")
        {
            if (!access.Student || input.StudentId != Actor || input.TargetType != "STUDENT") throw new ForbiddenException();
            var window = await db.Set<ResultAppealWindow>().SingleOrDefaultAsync(w => w.ProjectId == projectId && w.Id == input.WindowId, ct);
            if (window is null || window.PolicyId != input.PolicyId || window.Status != "OPEN" || Now < window.OpensAt || Now >= window.ClosesAt
                || !JsonSerializer.Deserialize<AppealWindowTarget[]>(window.TargetsJson)!.Contains(new(input.TargetType, input.StudentId, input.ResultVersion)))
                throw new ConflictException("No open authorized window for this version.", "APPEAL_WINDOW_CLOSED");
        }
        else if (input.Source == "STAFF_CORRECTION") { Lead(access); if (input.WindowId is not null) throw Invalid("Staff correction has no student window."); }
        else throw Invalid("Specify STUDENT_APPEAL or STAFF_CORRECTION.");
        var current = await revisions.Current(projectId, ct);
        var target = current.SingleOrDefault(t => t.Key == key) ?? throw new NotFoundException("PublishedResult", key);
        if (target.Version != input.ResultVersion) throw new ConflictException("Published version changed.", "RESULT_REVISION_STALE");
        ResultRevisionCalculator.Calculate(target.Inputs, []);
        using var root = JsonDocument.Parse(target.Inputs);
        var scheme = root.RootElement.GetProperty("Scheme").Deserialize<EvaluationSchemeDto>()!;
        var component = scheme.Components.SingleOrDefault(c => c.Id == input.ComponentId) ?? throw new NotFoundException("Component", input.ComponentId);
        var student = key == 0 ? null : scheme.Students.Single(s => s.StudentId == key);
        if (key == 0 ? component.ProjectWeightPercent <= 0 : component.StudentWeightPercent <= 0 || component.Scope != "COMMON" && component.MajorId != student!.MajorId)
            throw new ForbiddenException("Component is outside the selected published target.");
        var frozenScope = ProjectAcademicScopeReader.ParseEvidence(root.RootElement.GetProperty("RegistrationSnapshotJson").GetString()!)
            ?? throw new ConflictException("Historical scope is unknown.", "RESULT_PROVENANCE_UNKNOWN");
        var department = component.Scope == "COMMON" ? frozenScope.Scope.LeadDepartmentId : frozenScope.MajorDepartmentIds![component.MajorId!.Value];
        if (await db.Set<ResultAppealRequest>().AnyAsync(r => r.ProjectId == projectId && r.TargetKey == key && r.ResultVersion == input.ResultVersion
            && (r.Status == "PENDING" || r.Status == "UNDER_REVIEW"), ct)) throw new ConflictException("A request is already being processed for this result version.", "APPEAL_DUPLICATE");
        var row = new ResultAppealRequest { ProjectId = projectId, PolicyId = input.PolicyId, WindowId = input.WindowId, Source = input.Source,
            TargetKey = key, StudentId = input.StudentId, ResultVersion = input.ResultVersion, ComponentId = component.Id,
            Scope = component.Scope, MajorId = component.MajorId, ResponsibleDepartmentId = department, FrozenInputs = target.Inputs,
            BaseStamp = ResultRevisionStore.Stamp(current), RequestedBy = Actor, RequestedAt = Now, Reason = reason };
        db.Add(row); await db.SaveChangesAsync(ct); await Event(row, "APPEAL_REQUESTED", reason, ct);
        return await Dto(row, access, ct);
    }, ct);
}
