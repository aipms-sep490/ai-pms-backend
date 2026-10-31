using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Queries;
using AIPMS.Application.Features.Supervisors.Services;
using AIPMS.Domain.Supervisors;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class SupervisorRecommendationService(AipmsDbContext db, SupervisorCandidatePolicy policy,
    ISupervisorCandidateRepository candidates, SupervisorAccessService access, IEvaluationDraftRepository transactions,
    IAuditTrail audit, TimeProvider clock, IOptions<SupervisorRecommendationSettings> settings) : ISupervisorRecommendationService
{
    private const string Engine = "rules-v1";
    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["recommendations"] = [message] });
    private static void Paging(int page, int size)
    { if (page is < 1 or > 100000 || size is < 1 or > 100) throw Invalid("page 1..100000 and pageSize 1..100 required."); }
    private static void Validate(SupervisorRecommendationRequest r)
    {
        Paging(r.Page, r.PageSize);
        if (r.AssignmentType is not ("PRIMARY" or "DISCIPLINE_MENTOR")
            || (r.AssignmentType == "PRIMARY" ? r.MajorId is not null : r.MajorId is not > 0)
            || r.Search?.Length > 255) throw Invalid("Valid assignmentType/majorId and search <=255 required.");
    }
    private sealed record RecommendationAccess(SupervisorAccount Actor, ProjectAcademicScope Scope, bool Broad,
        IReadOnlyList<long> MentorMajors);
    private async Task<RecommendationAccess> Authorize(long projectId, CancellationToken ct)
    {
        if (!settings.Value.Enabled) throw new NotFoundException("SupervisorRecommendationsFeature", projectId);
        await policy.Read(projectId, ct);
        var actor = await access.EnsureCanReadAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: true);
        if (scope.Provenance != "FROZEN_REGISTRATION_SNAPSHOT")
            throw new ConflictException("Frozen academic scope is required.", WorkflowErrorCodes.AcademicScopeUnknown);
        var assignments = await db.SupervisorAssignments.AsNoTracking().Where(a => a.ProjectId == projectId
            && a.EndedAt == null && a.SupervisorProfile.UserId == actor.UserId)
            .Select(a => new { a.IsPrimary, a.AssignmentType, a.MajorId }).ToArrayAsync(ct);
        var member = actor.Roles.Contains(AppRoles.Student) && await db.TeamMembers.AnyAsync(m => m.LeftAt == null
            && m.UserId == actor.UserId && m.Team.Project != null && m.Team.Project.Id == projectId, ct);
        return new(actor, scope, actor.Roles.Contains(AppRoles.Admin) || member
            || (actor.Roles.Contains(AppRoles.DepartmentStaff) && actor.DepartmentId == scope.LeadDepartmentId)
            || (actor.Roles.Contains(AppRoles.Lecturer) && assignments.Any(a => a.IsPrimary && a.AssignmentType == "PRIMARY")),
            actor.Roles.Contains(AppRoles.Lecturer) ? assignments.Where(a => a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId.HasValue)
                .Select(a => a.MajorId!.Value).ToArray() : []);
    }
    private static bool CanReadSlot(RecommendationAccess access, string type, long? major)
    {
        var (actor, scope, broad, mentorMajors) = access;
        if (type == "DISCIPLINE_MENTOR" && !scope.MajorIds.Contains(major ?? 0)) return false;
        if (broad) return true;
        return type == "DISCIPLINE_MENTOR" && major.HasValue && (mentorMajors.Contains(major.Value)
            || (actor.Roles.Contains(AppRoles.DepartmentStaff) && scope.MajorDepartmentIds is not null
                && scope.MajorDepartmentIds.TryGetValue(major.Value, out var department) && actor.DepartmentId == department));
    }
    private static GetSupervisorCandidatesQuery Query(long id, SupervisorRecommendationRequest r) =>
        new(id, r.Search, null, r.Page, r.PageSize, r.AssignmentType, r.MajorId);
    private static SupervisorCandidateSearch Search(SupervisorCandidateContext c, SupervisorRecommendationRequest r,
        IReadOnlyList<long>? profileIds = null) => new(c.Project.Id, c.Project.AcademicSemesterId, c.Project.DepartmentIds,
        c.Policy.MaxProjectsPerSupervisor!.Value, r.Search?.Trim(), null, r.Page, r.PageSize, r.AssignmentType, r.MajorId,
        RankingMajorIds: r.AssignmentType == "PRIMARY" ? c.Project.RequiredMajorIds : [r.MajorId!.Value], ProfileIds: profileIds);
    private static SupervisorRecommendationItem Map(SupervisorCandidateModel c, SupervisorCandidateContext selection,
        SupervisorRecommendationRequest request, ProjectAcademicScope scope)
    {
        var majors = selection.Project.MajorScopes!.Where(m => request.MajorId is null || m.Id == request.MajorId).ToArray();
        var matched = c.Profile.Expertise.Where(e => majors.Any(m => SupervisorExpertiseMatch.Matches(e.Name, m.Code, m.Name)))
            .Select(e => e.Name).Distinct().Order(StringComparer.Ordinal).ToArray();
        var matchCount = majors.Count(m => c.Profile.Expertise.Any(e => SupervisorExpertiseMatch.Matches(e.Name, m.Code, m.Name)));
        var status = c.Profile.Expertise.Count == 0 ? "UNKNOWN" : matchCount > 0 ? "MATCHED" : "NO_MATCH";
        var cap = new SupervisorCapacity(c.ProfileLimit, selection.Policy.MaxProjectsPerSupervisor!.Value,
            c.ActiveProjects - (c.AlreadyAssignedToProject ? 1 : 0), c.SemesterActiveProjects - (c.AlreadyAssignedToProject ? 1 : 0));
        var dto = new SupervisorCandidateDto(c.Profile.Id, c.Profile.UserId, c.Profile.FullName,
            c.Profile.DepartmentId, c.Profile.DepartmentName, c.Profile.Bio,
            c.Profile.Expertise.Select(e => new SupervisorExpertiseDto(e.Name, e.ProficiencyLevel)).ToArray(),
            c.ActiveProjects, c.SemesterActiveProjects, c.ProfileLimit, cap.SemesterLimit, cap.RemainingSlots,
            selection.Policy.PeriodId, request.AssignmentType, request.MajorId,
            request.MajorId is long major ? scope.MajorDepartmentIds![major] : scope.LeadDepartmentId, true, []);
        return new(dto, majors.Length == 0 ? 0 : (decimal)matchCount / majors.Length, status, matched,
            ["ELIGIBLE_AT_GENERATION", "EXPERTISE_" + status, "CAPACITY_AVAILABLE", "ADVISORY_ONLY"], true, []);
    }
    public async Task<SupervisorRecommendationPreview> Preview(long projectId, SupervisorRecommendationRequest request, CancellationToken ct)
    {
        Validate(request);
        var authorization = await Authorize(projectId, ct);
        if (!CanReadSlot(authorization, request.AssignmentType, request.MajorId)) throw new ForbiddenException();
        var selection = await policy.Select(Query(projectId, request), ct);
        var result = await candidates.SearchAsync(Search(selection, request), ct);
        return new(Engine, request.AssignmentType, request.MajorId, clock.GetUtcNow().UtcDateTime,
            new(result.Items.Select(c => Map(c, selection, request, authorization.Scope)).ToArray(), result.Page, result.PageSize, result.TotalCount));
    }
    public Task<SupervisorRecommendationRun> Create(long projectId, SupervisorRecommendationRequest request, CancellationToken ct) =>
        transactions.InTransactionAsync(async () => {
            await transactions.LockProjectAsync(projectId, ct);
            var preview = await Preview(projectId, request, ct);
            var actor = await access.EnsureCanReadAsync(ct);
            var row = new SupervisorRecommendation { ProjectId = projectId, CreatedBy = actor.UserId,
                CreatedAt = preview.GeneratedAt, AssignmentType = request.AssignmentType, MajorId = request.MajorId,
                Search = request.Search?.Trim(), Page = request.Page, PageSize = request.PageSize, TotalCount = preview.Candidates.TotalCount,
                Items = preview.Candidates.Items.Select((item, i) => new SupervisorRecommendationEntry {
                    Position = i + 1, SupervisorProfileId = item.Candidate.Id, SnapshotJson = JsonSerializer.Serialize(item) }).ToArray() };
            db.Add(row); await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new(actor.UserId, "SUPERVISOR_RECOMMENDATIONS_CREATED", "SUPERVISOR_RECOMMENDATION_RUN", row.Id,
                new Dictionary<string, object?> { ["projectId"] = projectId, ["assignmentType"] = request.AssignmentType,
                    ["majorId"] = request.MajorId, ["engineVersion"] = Engine, ["itemCount"] = row.Items.Count }), ct);
            return Dto(row, preview.Candidates.Items);
        }, ct);
    private static SupervisorRecommendationRun Dto(SupervisorRecommendation row, IReadOnlyList<SupervisorRecommendationItem> items) =>
        new(row.Id, row.ProjectId, row.CreatedBy, DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc), row.EngineVersion,
            row.AssignmentType, row.MajorId, row.Search, row.Page, row.PageSize, row.TotalCount, items);
    public async Task<SupervisorRecommendationRun> Get(long projectId, long runId, CancellationToken ct)
    {
        var authorization = await Authorize(projectId, ct);
        var row = await db.Set<SupervisorRecommendation>().AsNoTracking().Include(r => r.Items)
            .SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Id == runId, ct) ?? throw new NotFoundException("RecommendationRun", runId);
        if (!CanReadSlot(authorization, row.AssignmentType, row.MajorId)) throw new ForbiddenException();
        var old = row.Items.OrderBy(i => i.Position).Select(i => JsonSerializer.Deserialize<SupervisorRecommendationItem>(i.SnapshotJson)!).ToArray();
        var eligible = new HashSet<long>();
        var reason = "ELIGIBILITY_CHANGED";
        try
        {
            var request = new SupervisorRecommendationRequest(row.AssignmentType, row.MajorId, PageSize: 100);
            var selection = await policy.Select(Query(projectId, request), ct);
            if (old.Length > 0)
                eligible = (await candidates.SearchAsync(Search(selection, request, old.Select(i => i.Candidate.Id).ToArray()), ct))
                    .Items.Select(i => i.Profile.Id).ToHashSet();
        }
        catch (ConflictException) { reason = "SELECTION_NOT_AVAILABLE"; }
        return Dto(row, old.Select(i => i with { CurrentlyEligible = eligible.Contains(i.Candidate.Id),
            CurrentReasons = eligible.Contains(i.Candidate.Id) ? [] : [reason] }).ToArray());
    }
    public async Task<PagedResult<SupervisorRecommendationRunSummary>> History(long projectId, int page, int pageSize, CancellationToken ct)
    {
        Paging(page, pageSize);
        var authorization = await Authorize(projectId, ct);
        var query = db.Set<SupervisorRecommendation>().AsNoTracking().Where(r => r.ProjectId == projectId);
        if (!authorization.Broad)
        {
            var ids = authorization.Scope.MajorIds.Where(m => CanReadSlot(authorization, "DISCIPLINE_MENTOR", m)).ToArray();
            query = query.Where(r => r.AssignmentType == "DISCIPLINE_MENTOR" && r.MajorId.HasValue && ids.Contains(r.MajorId.Value));
        }
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new SupervisorRecommendationRunSummary(r.Id, DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                r.CreatedBy, r.AssignmentType, r.MajorId, r.EngineVersion, r.Items.Count, r.TotalCount)).ToArrayAsync(ct);
        return new(rows, page, pageSize, count);
    }
}
