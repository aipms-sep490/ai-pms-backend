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
    IOptions<AppealSettings> settings, IDeliverableRepository files, IFileStorage storage, ILogger<ResultAppealService> logger) : IAppealService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private sealed record Access(EvaluationActor Actor, EvaluationProject Project, bool Lead, bool Staff, bool Student, bool Primary);
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
    private async Task<Access> Scope(long projectId, CancellationToken ct)
    {
        if (!settings.Value.Enabled) throw new NotFoundException("AppealFeature", projectId);
        var actor = await transactions.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await transactions.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (!project.ActiveScope || project.ScopeProvenance != "FROZEN_REGISTRATION_SNAPSHOT")
            throw new ConflictException("Frozen academic scope required.", WorkflowErrorCodes.AcademicScopeUnknown);
        var student = await db.Set<StudentResult>().AnyAsync(r => r.ProjectId == projectId && r.StudentId == Actor, ct)
            && await db.UserRoles.AnyAsync(r => r.UserId == Actor && r.Role.Code == "STUDENT", ct);
        var primary = await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null && a.IsPrimary
            && a.AssignmentType == "PRIMARY" && a.SupervisorProfile.UserId == Actor, ct);
        return new(actor, project, AcademicAuthority.IsLead(actor, project), AcademicAuthority.HasScope(actor, project), student, primary);
    }
    private static bool DepartmentAccess(ResultAppealRequest r, Access access) => access.Staff
        && (access.Lead || r.Scope != "COMMON" && r.ResponsibleDepartmentId == access.Actor.DepartmentId);
    private async Task Reviewer(ResultAppealRequest row, long reviewerId, CancellationToken ct)
    {
        await LoadReviewers(row.ProjectId, [reviewerId], ct);
        if (!ReviewerEligible(row, reviewerId)) throw new ForbiddenException("Reviewer must be in scope, independent of original scorers and not the Primary.");
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
        var primaryIds = await db.SupervisorAssignments.Where(a => a.ProjectId == projectId && a.EndedAt == null && a.IsPrimary
            && a.AssignmentType == "PRIMARY" && missing.Contains(a.SupervisorProfile.UserId)).Select(a => a.SupervisorProfile.UserId).ToArrayAsync(ct);
        foreach (var id in missing) reviewerStates[id] = (actors.SingleOrDefault(a => a.Id == id), primaryIds.Contains(id));
    }
    private Task Read(ResultAppealRequest row, Access access, CancellationToken ct)
    {
        if (row.Source == "STUDENT_APPEAL" && row.RequestedBy == Actor && access.Student || DepartmentAccess(row, access)) return Task.CompletedTask;
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
    private static (EvaluationSchemeDto Scheme, SchemeComponentDto Component, EvaluationDraftRecord[] Evaluations) Frozen(ResultAppealRequest row)
    {
        using var root = JsonDocument.Parse(row.FrozenInputs);
        var scheme = root.RootElement.GetProperty("Scheme").Deserialize<EvaluationSchemeDto>()!;
        var component = scheme.Components.Single(c => c.Id == row.ComponentId);
        var evaluations = root.RootElement.GetProperty("Evaluations").EnumerateArray().Where(e => {
            using var scope = JsonDocument.Parse(e.GetProperty("ScopeSnapshotJson").GetString()!);
            return scope.RootElement.GetProperty("component").GetProperty("Id").GetInt64() == row.ComponentId
                && (row.Scope != "INDIVIDUAL" || scope.RootElement.GetProperty("StudentId").GetInt64() == row.StudentId);
        }).Select(e => e.GetProperty("evaluation").Deserialize<EvaluationDraftRecord>()!).DistinctBy(e => e.Id).ToArray();
        return (scheme, component, evaluations);
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
        await audit.RecordAsync(new(Actor, action, "RESULT_APPEAL_REQUEST", row.Id, new Dictionary<string, object?> {
            ["projectId"] = row.ProjectId, ["resultVersion"] = row.ResultVersion, ["componentId"] = row.ComponentId,
            ["status"] = row.Status }), ct);
        if (action is "APPEAL_REQUESTED" or "APPEAL_REVIEWER_ASSIGNED" or "APPEAL_REVIEWED" or "APPEAL_DECIDED")
            await Notify(row, action, ct);
    }
    private async Task<CorrectionRequestDto> Dto(ResultAppealRequest row, Access access, CancellationToken ct)
    {
        var actions = new List<string> { "READ" }; var blockers = new List<string>();
        var open = row.Status is "PENDING" or "UNDER_REVIEW";
        if (open)
        {
            try { await Current(row, ct); } catch (ConflictException) { blockers.Add("RESULT_REVISION_STALE"); }
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
            if (row.RequestedBy == Actor && row.Status == "PENDING" && blockers.Count == 0)
            {
                await LoadWindows(row.WindowId.HasValue ? [row.WindowId.Value] : [], ct);
                if (row.Source == "STAFF_CORRECTION" || row.WindowId is long id && windowStates.GetValueOrDefault(id)) actions.Add("UPLOAD");
                else blockers.Add("APPEAL_WINDOW_CLOSED");
            }
        }
        return new(row.Id, row.ProjectId, row.Source, row.TargetKey == 0 ? "PROJECT" : "STUDENT", row.StudentId,
            row.ResultVersion, row.ComponentId, row.Scope, row.MajorId, row.Status, row.RequestedBy, row.ReviewerId,
            row.Reason, row.Proposal, row.ReviewReason, row.DecisionReason, row.ConcurrencyToken.ToString("N"), actions, blockers,
            row.Files.OrderBy(f => f.FileId).Select(f => new AppealFileDto(f.FileId, f.FileName, f.ContentType, f.SizeBytes,
                f.Sha256, $"/api/v1/projects/{row.ProjectId}/result/correction-requests/{row.Id}/files/{f.FileId}")).ToArray());
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
                || r.ReviewerId == Actor && !access.Primary && (access.Actor.IsLecturer || access.Actor.IsStaff)
                    && r.ResponsibleDepartmentId == access.Actor.DepartmentId));
        var rows = await query.Include(r => r.Files).OrderByDescending(r => r.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        await LoadReviewers(projectId, rows.Where(r => r.ReviewerId.HasValue).Select(r => r.ReviewerId!.Value), ct);
        await LoadWindows(rows.Where(r => r.WindowId.HasValue).Select(r => r.WindowId!.Value), ct);
        var result = new List<CorrectionRequestDto>();
        foreach (var row in rows) { await Read(row, access, ct); result.Add(await Dto(row, access, ct)); }
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
