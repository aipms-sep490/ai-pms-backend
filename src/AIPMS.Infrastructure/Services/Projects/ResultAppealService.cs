using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Appeals;
using AIPMS.Application.Features.Appeals.Abstractions;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ResultAppealService(AipmsDbContext db, IEvaluationDraftRepository transactions,
    ResultRevisionStore revisions, ICurrentUser current, IAuditTrail audit, TimeProvider clock,
    IOptions<AppealSettings> settings, IDeliverableRepository files, IFileStorage storage,
    IFileSecurityScanner scanner, ILogger<ResultAppealService> logger) : IAppealService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private sealed record Access(EvaluationActor Actor, EvaluationProject Project, bool Lead, bool Staff, bool Student, bool Primary, bool ActivePrimarySupervisor);
    private ResultRevisionStore.Target[]? effective;
    private readonly Dictionary<long, (EvaluationActor? Actor, bool Primary)> reviewerStates = [];
    private readonly Dictionary<long, bool> windowStates = [];
    private async Task<ResultRevisionStore.Target[]> Effective(long projectId, CancellationToken ct) => effective ??= await revisions.Current(projectId, ct);
    private static ValidationException Invalid(string value) => new(new Dictionary<string, string[]> { ["appeal"] = [value] });
    private static void Page(int page, int size) { if (page is < 1 or > 1000 || size is < 1 or > 100) throw Invalid("page 1..1000, pageSize 1..100 required."); }
    private static string Reason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 2000 ? reason.Trim() : throw Invalid("Reason 1..2000 required.");
    private static void Token(Guid actual, string token) { if (!Guid.TryParse(token, out var parsed) || parsed != actual) throw new ConflictException("Reload the appeal before retrying.", "APPEAL_STALE"); }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime Seconds(DateTimeOffset value) => new(value.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    private static void Lead(Access access) { if (!access.Lead) throw new ForbiddenException("The frozen lead department manages appeals."); }

    private static long[] ComponentIdsOf(ResultAppealRequest row)
    {
        if (!string.IsNullOrWhiteSpace(row.ComponentIdsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<long[]>(row.ComponentIdsJson);
                if (parsed is { Length: > 0 }) return parsed.Distinct().ToArray();
            }
            catch { }
        }
        return [row.ComponentId];
    }

    private async Task<Access> Scope(long projectId, CancellationToken ct)
    {
        if (!settings.Value.Enabled) throw new NotFoundException("AppealFeature", projectId);
        var actor = await transactions.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await transactions.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (!project.ActiveScope || project.ScopeProvenance != "FROZEN_REGISTRATION_SNAPSHOT")
            throw new ConflictException("Frozen academic scope required.", "ACADEMIC_SCOPE_UNKNOWN");
        var student = await db.Set<StudentResult>().AnyAsync(r => r.ProjectId == projectId && r.StudentId == Actor, ct)
            && await db.UserRoles.AnyAsync(r => r.UserId == Actor && r.Role.Code == "STUDENT", ct);
        // Extended D17 (Spec V4.0 §7.4 / QD-06): Block active and former Primary Supervisors AND Discipline Mentors from appeal review.
        var primary = await AcademicAssignmentGuard.HasSupervisorOrMentorConflictAsync(db, projectId, Actor, ct);
        var activePrimary = actor.IsLecturer && await db.SupervisorAssignments.AsNoTracking().AnyAsync(
            a => a.ProjectId == projectId && a.EndedAt == null && a.IsPrimary && a.AssignmentType == "PRIMARY"
                && a.SupervisorProfile.UserId == Actor, ct);
        return new(actor, project, AcademicAuthority.IsLead(actor, project), AcademicAuthority.HasScope(actor, project), student, primary, activePrimary);
    }

    private static bool DepartmentAccess(ResultAppealRequest r, Access access) => access.Staff
        && (access.Lead || r.Scope != "COMMON" && r.ResponsibleDepartmentId == access.Actor.DepartmentId);

    private async Task Reviewer(ResultAppealRequest row, long reviewerId, CancellationToken ct)
    {
        await LoadReviewers(row.ProjectId, [reviewerId], ct);
        var state = reviewerStates[reviewerId];
        if (state.Primary)
            throw new ConflictException("Active or former primary supervisors and discipline mentors cannot review appeals for this project (D17/QD-06).", WorkflowErrorCodes.EvaluatorRoleConflict);
        if (!ReviewerEligible(row, reviewerId))
            throw new ForbiddenException("Reviewer must be in scope, independent of original scorers and not an active or former supervisor/mentor.");
    }

    private bool ReviewerEligible(ResultAppealRequest row, long id)
    {
        var state = reviewerStates[id]; var actor = state.Actor;
        return actor is not null && id != row.StudentId && (actor.IsLecturer || actor.IsStaff) && !state.Primary
            && actor.DepartmentId == row.ResponsibleDepartmentId && !Scorers(row).Contains(id);
    }

    private async Task LoadReviewers(long projectId, IEnumerable<long> ids, CancellationToken ct)
    {
        var missing = ids.Distinct().Where(id => !reviewerStates.ContainsKey(id)).ToArray(); if (missing.Length == 0) return;
        var actors = await db.Users.AsNoTracking().Where(u => missing.Contains(u.Id) && u.Status == "ACTIVE"
            && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive)
            .Select(u => new EvaluationActor(u.Id, u.DepartmentId, false,
                u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER"))).ToArrayAsync(ct);
        var primaryIds = await AcademicAssignmentGuard.GetConflictingSupervisorOrMentorUserIdsAsync(db, projectId, missing, ct);
        foreach (var id in missing) reviewerStates[id] = (actors.SingleOrDefault(a => a.Id == id), primaryIds.Contains(id));
    }

    private Task Read(ResultAppealRequest row, Access access, CancellationToken ct)
    {
        if (row.Source == "STUDENT_APPEAL" && row.RequestedBy == Actor && access.Student || DepartmentAccess(row, access)) return Task.CompletedTask;
        if (row.AppealKind == "PROCESS_20_ERRATA" && access.ActivePrimarySupervisor) return Task.CompletedTask;
        if (row.ReviewerId == Actor && EligibleReviewer(row, access)) return Task.CompletedTask;
        throw new ForbiddenException();
    }

    private static bool EligibleReviewer(ResultAppealRequest row, Access access) => !access.Primary
        && row.StudentId != access.Actor.Id
        && (access.Actor.IsLecturer || access.Actor.IsStaff) && access.Actor.DepartmentId == row.ResponsibleDepartmentId
        && !Scorers(row).Contains(access.Actor.Id);

    private async Task<ResultAppealRequest> Row(long projectId, long id, CancellationToken ct) =>
        await db.Set<ResultAppealRequest>().Include(r => r.Files).SingleOrDefaultAsync(r => r.Id == id && r.ProjectId == projectId, ct)
        ?? throw new NotFoundException("CorrectionRequest", id);

    private static (EvaluationSchemeDto Scheme, SchemeComponentDto Component, SchemeComponentDto[] Components, EvaluationDraftRecord[] Evaluations, AppealReviewGateScoreDto[] GateScores) Frozen(ResultAppealRequest row)
    {
        using var root = JsonDocument.Parse(row.FrozenInputs);
        var scheme = root.RootElement.GetProperty("Scheme").Deserialize<EvaluationSchemeDto>()!;
        var targetComponentIds = ComponentIdsOf(row).ToHashSet();
        var components = scheme.Components.Where(c => targetComponentIds.Contains(c.Id)).ToArray();
        var primaryComponent = scheme.Components.Single(c => c.Id == row.ComponentId);
        var evaluations = new List<EvaluationDraftRecord>();
        var gateScores = new List<AppealReviewGateScoreDto>();

        foreach (var e in root.RootElement.GetProperty("Evaluations").EnumerateArray())
        {
            // TK-W4-01/02: Safely partition GATE_INDIVIDUAL_PROCESS, DEFENSE_ABSENT, and standard Rubric entries.
            if (e.TryGetProperty("Type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
            {
                if (typeProp.GetString() == "GATE_INDIVIDUAL_PROCESS"
                    && e.TryGetProperty("StudentId", out var stuProp)
                    && (!row.StudentId.HasValue || stuProp.GetInt64() == row.StudentId.Value)
                    && e.TryGetProperty("Gates", out var gatesArr) && gatesArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in gatesArr.EnumerateArray())
                    {
                        gateScores.Add(new(
                            g.GetProperty("Id").GetInt64(),
                            g.GetProperty("GateNumber").GetInt32(),
                            g.GetProperty("Score").GetDecimal(),
                            g.GetProperty("GateWeight").GetDecimal()));
                    }
                }
                continue;
            }
            if (!e.TryGetProperty("ScopeSnapshotJson", out var scopeStr) || scopeStr.ValueKind != JsonValueKind.String
                || !e.TryGetProperty("evaluation", out var evalProp))
                continue;
            using var scope = JsonDocument.Parse(scopeStr.GetString()!);
            var compId = scope.RootElement.GetProperty("component").GetProperty("Id").GetInt64();
            if (!targetComponentIds.Contains(compId)) continue;
            var comp = scheme.Components.Single(c => c.Id == compId);
            if (comp.Scope == "INDIVIDUAL" && scope.RootElement.GetProperty("StudentId").GetInt64() != row.StudentId)
                continue;
            var parsedEval = evalProp.Deserialize<EvaluationDraftRecord>();
            if (parsedEval is not null) evaluations.Add(parsedEval);
        }

        return (scheme, primaryComponent, components, evaluations.DistinctBy(e => e.Id).ToArray(),
            gateScores.DistinctBy(g => g.GateScoreId).OrderBy(g => g.GateNumber).ToArray());
    }

    private static long[] Scorers(ResultAppealRequest row) => Frozen(row).Evaluations.Select(e => e.EvaluatorId).Distinct().ToArray();

    private async Task Current(ResultAppealRequest row, CancellationToken ct)
    {
        if (ResultRevisionStore.Stamp(await Effective(row.ProjectId, ct)) != row.BaseStamp)
            throw new ConflictException("Published results changed. Close this request and review the new version explicitly.", "RESULT_REVISION_STALE");
    }

    private async Task Event(ResultAppealRequest row, string action, string? reason, CancellationToken ct)
    {
        db.Add(new ResultAppealEvent { RequestId = row.Id, ActorId = Actor, BundleId = row.BundleId, Action = action, At = Now, Reason = reason });
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, action, "RESULT_APPEAL_REQUEST", row.Id, new Dictionary<string, object?>
        {
            ["projectId"] = row.ProjectId, ["resultVersion"] = row.ResultVersion, ["componentId"] = row.ComponentId,
            ["appealKind"] = row.AppealKind, ["status"] = row.Status
        }), ct);
        if (action is "APPEAL_REQUESTED" or "APPEAL_REVIEWER_ASSIGNED" or "APPEAL_REVIEWED" or "APPEAL_DECIDED" or "APPEAL_SUPERVISOR_ERRATA")
            await Notify(row, action, ct);
    }

    private async Task<CorrectionRequestDto> Dto(ResultAppealRequest row, Access access, CancellationToken ct) =>
        (await DtosAsync([row], access, ct))[0];

    private async Task<CorrectionRequestDto[]> DtosAsync(IReadOnlyList<ResultAppealRequest> rows, Access access, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var userIds = rows.Select(r => r.RequestedBy)
            .Concat(rows.Where(r => r.ReviewerId.HasValue).Select(r => r.ReviewerId!.Value))
            .Concat(rows.Where(r => r.StudentId.HasValue).Select(r => r.StudentId!.Value))
            .Distinct().ToArray();
        var majorIds = rows.Where(r => r.MajorId.HasValue).Select(r => r.MajorId!.Value).Distinct().ToArray();
        var userNames = userIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var majorNames = majorIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Majors.AsNoTracking().Where(m => majorIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var allComponentIds = rows.SelectMany(ComponentIdsOf).Distinct().ToArray();
        var componentNames = allComponentIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Set<EvaluationSchemeComponent>().AsNoTracking().Where(c => allComponentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var result = new List<CorrectionRequestDto>(rows.Count);
        foreach (var row in rows)
        {
            var actions = new List<string> { "READ" }; var blockers = new List<string>();
            var open = row.Status is "PENDING" or "UNDER_REVIEW";
            if (open)
            {
                try { await Current(row, ct); } catch (ConflictException) { blockers.Add("RESULT_REVISION_STALE"); }

                if (row.AppealKind == "INDEPENDENT_80")
                {
                    if (row.ReviewerId is long reviewer)
                    { await LoadReviewers(row.ProjectId, [reviewer], ct); if (!ReviewerEligible(row, reviewer)) blockers.Add("REVIEWER_INELIGIBLE"); }
                    if (access.Lead && !blockers.Contains("RESULT_REVISION_STALE")) actions.Add("ASSIGN_REVIEWER");
                    if (row.ReviewerId == Actor && blockers.Count == 0)
                    { if (EligibleReviewer(row, access)) actions.Add("REVIEW"); else blockers.Add("REVIEWER_INELIGIBLE"); }
                    if (access.Lead && row.ReviewerId != Actor && !Scorers(row).Contains(Actor))
                    {
                        actions.Add("REJECT");
                        if (row.Proposal == "AMEND" && blockers.Count == 0) { actions.Add("PREVIEW"); actions.Add("ACCEPT"); }
                    }
                }
                else if (row.AppealKind == "PROCESS_20_ERRATA")
                {
                    if (access.ActivePrimarySupervisor && !blockers.Contains("RESULT_REVISION_STALE"))
                        actions.Add("SUPERVISOR_ERRATA");
                    if (access.Lead)
                    {
                        actions.Add("REJECT");
                        if (row.Proposal == "AMEND" && row.BundleId.HasValue && blockers.Count == 0)
                        { actions.Add("PREVIEW"); actions.Add("ACCEPT"); }
                    }
                }
                else if (row.AppealKind == "DEFENSE_ABSENT_CONTEST")
                {
                    if (access.Lead)
                    {
                        actions.Add("REJECT");
                        if (!blockers.Contains("RESULT_REVISION_STALE")) actions.Add("ACCEPT");
                    }
                }

                if (row.RequestedBy == Actor && row.Status == "PENDING" && row.Source == "STUDENT_APPEAL") actions.Add("WITHDRAW");
                if (row.RequestedBy == Actor && row.Status == "PENDING" && blockers.Count == 0)
                {
                    await LoadWindows(row.WindowId.HasValue ? [row.WindowId.Value] : [], ct);
                    if (row.Source == "STAFF_CORRECTION" || row.WindowId is long id && windowStates.GetValueOrDefault(id)) actions.Add("UPLOAD");
                    else blockers.Add("APPEAL_WINDOW_CLOSED");
                }
            }

            var compIds = ComponentIdsOf(row);
            string? componentName = null;
            string[] compNames;
            try
            {
                var frozen = Frozen(row);
                componentName = frozen.Component.Name;
                compNames = frozen.Components.Select(c => c.Name).ToArray();
            }
            catch
            {
                componentName = componentNames.GetValueOrDefault(row.ComponentId);
                compNames = compIds.Select(cid => componentNames.GetValueOrDefault(cid, $"#{cid}")).ToArray();
            }

            result.Add(new(row.Id, row.ProjectId, row.Source, row.TargetKey == 0 ? "PROJECT" : "STUDENT", row.StudentId,
                row.ResultVersion, row.ComponentId, row.Scope, row.MajorId, row.Status, row.RequestedBy, row.ReviewerId,
                row.Reason, row.Proposal, row.ReviewReason, row.DecisionReason, row.ConcurrencyToken.ToString("N"), actions, blockers,
                row.Files.OrderBy(f => f.FileId).Select(f => new AppealFileDto(f.FileId, f.FileName, f.ContentType, f.SizeBytes,
                    f.Sha256, $"/api/v1/projects/{row.ProjectId}/result/correction-requests/{row.Id}/files/{f.FileId}")).ToArray(),
                userNames.GetValueOrDefault(row.RequestedBy),
                row.ReviewerId.HasValue ? userNames.GetValueOrDefault(row.ReviewerId.Value) : null,
                row.StudentId.HasValue ? userNames.GetValueOrDefault(row.StudentId.Value) : null,
                componentName,
                row.MajorId.HasValue ? majorNames.GetValueOrDefault(row.MajorId.Value) : null,
                row.AppealKind,
                compIds,
                compNames,
                row.SupervisorNote,
                row.SupervisorRespondedAt.HasValue ? Utc(row.SupervisorRespondedAt.Value) : null));
        }
        return result.ToArray();
    }

    public async Task<CorrectionRequestDto> Get(long projectId, long id, CancellationToken ct)
    { var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct); await Read(row, access, ct); return await Dto(row, access, ct); }

    public async Task<PagedResult<CorrectionRequestDto>> List(long projectId, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); var access = await Scope(projectId, ct);
        if (!access.Student && !access.Staff && !access.Actor.IsLecturer) throw new ForbiddenException();
        var query = db.Set<ResultAppealRequest>().AsNoTracking().Where(r => r.ProjectId == projectId
            && (access.Lead || access.Staff && r.Scope != "COMMON" && r.ResponsibleDepartmentId == access.Actor.DepartmentId
                || access.Student && r.RequestedBy == Actor && r.Source == "STUDENT_APPEAL"
                || access.ActivePrimarySupervisor && r.AppealKind == "PROCESS_20_ERRATA"
                || r.ReviewerId == Actor && !access.Primary && (access.Actor.IsLecturer || access.Actor.IsStaff)
                    && r.ResponsibleDepartmentId == access.Actor.DepartmentId));
        var rows = await query.Include(r => r.Files).OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        await LoadReviewers(projectId, rows.Where(r => r.ReviewerId.HasValue).Select(r => r.ReviewerId!.Value), ct);
        await LoadWindows(rows.Where(r => r.WindowId.HasValue).Select(r => r.WindowId!.Value), ct);
        foreach (var row in rows) await Read(row, access, ct);
        var result = await DtosAsync(rows, access, ct);
        return new(result, page, pageSize, await query.CountAsync(ct));
    }

    private async Task LoadWindows(IEnumerable<long> ids, CancellationToken ct)
    {
        var missing = ids.Distinct().Where(id => !windowStates.ContainsKey(id)).ToArray(); if (missing.Length == 0) return;
        var rows = await db.Set<ResultAppealWindow>().AsNoTracking().Where(w => missing.Contains(w.Id))
            .Select(w => new { w.Id, Open = w.Status == "OPEN" && w.OpensAt <= Now && Now < w.ClosesAt }).ToArrayAsync(ct);
        foreach (var id in missing) windowStates[id] = rows.SingleOrDefault(w => w.Id == id)?.Open ?? false;
    }
}
