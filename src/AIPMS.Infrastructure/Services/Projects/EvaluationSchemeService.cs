using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.FinalSubmissions.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.Abstractions;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class EvaluationSchemeService(AipmsDbContext db, IEvaluationDraftRepository evaluations,
    IProjectResultRepository results, IFinalSubmissionRepository submissions, ICurrentUser current,
    IAuditTrail audit, IPublisher publisher, TimeProvider clock, ColdSubmissionService cold) : IEvaluationSchemeService
{
    public async Task<AIPMS.Application.Common.Models.PagedResult<EligibleEvaluatorDto>> CandidatesAsync(long projectId, long periodId,
        long componentId, string? scope, long? majorId, long? studentId, int page, int pageSize, CancellationToken ct)
    {
        await Manage(projectId, ct);
        var component = await db.Set<EvaluationSchemeComponent>().SingleOrDefaultAsync(c => c.Id == componentId, ct)
            ?? throw new NotFoundException("SchemeComponent", componentId);
        var scheme = await Row(component.SchemeId, ct);
        if (IsPol03IndividualProcess(component.Scope, component.Stage)
            && (scheme.CalculationRule == VersionedResultCalculator.PolV4Version || !component.RubricId.HasValue))
            throw new ConflictException(
                "The 20% individual process component (POL-03) is graded directly by the Primary Supervisor across Gates G1–G3 and cannot be assigned to external evaluators.",
                WorkflowErrorCodes.IndividualProcessManagedBySupervisor);
        if (scheme.ProjectId != projectId || scheme.ProjectPeriodId != periodId) throw new NotFoundException("SchemeComponent", componentId);
        if (scheme.Status != "PUBLISHED" || scope != component.Scope || majorId != component.MajorId
            || (scope == "INDIVIDUAL") != studentId.HasValue) throw new ConflictException("Select a published component and matching target.");
        if (studentId.HasValue && !JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson)!.Any(s => s.StudentId == studentId && s.MajorId == majorId))
            throw new NotFoundException("Student", studentId);
        var rubricId = component.RubricId ?? throw new ConflictException("Component requires a published rubric.");
        await ComponentAuthority(rubricId, projectId, ct);
        var period = await evaluations.GetPeriodAsync(periodId, Now, ct);
        var project = (await evaluations.GetProjectAsync(projectId, ct))!;
        if (period is null || !period.IsOpen || period.SemesterId != project.SemesterId)
            throw new ConflictException("Evaluation period is closed.", WorkflowErrorCodes.EvaluationWindowClosed);
        if (project.Status != "FINAL_SUBMISSION" || !await evaluations.HasLockedSubmissionAsync(projectId, ct))
            throw new ConflictException("A locked final-submission package is required.", WorkflowErrorCodes.FinalPackageRequired);
        if (component.Stage == "COLD") await ColdEvaluationBinding.Capture(db, projectId, component.Scope, component.MajorId, studentId, ct);
        if ((component.Stage is "COLD" or "DEFENSE") && !await db.Set<DefenseCommittee>().AnyAsync(
            c => c.ProjectId == projectId && c.Status == "CONFIRMED", ct))
            throw new ConflictException("Confirm the defense committee before assigning evaluators.", "DEFENSE_COMMITTEE_REQUIRED");
        var department = await db.Rubrics.Where(r => r.Id == rubricId).Select(r => r.DepartmentId!.Value).SingleAsync(ct);
        var slots = db.Set<EvaluationAssignment>().Where(a => a.ComponentId == componentId && a.StudentId == studentId && a.Status == "ACTIVE");
        if (await slots.CountAsync(ct) >= component.RequiredEvaluators) return new([], page, pageSize, 0);
        var allowedDeptIds = component.Scope == "COMMON" ? project.DepartmentIds.ToArray() : new[] { department };
        var query = db.Users.AsNoTracking().Where(u => u.Status == "ACTIVE" && u.DepartmentId != null && allowedDeptIds.Contains(u.DepartmentId.Value)
            && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive && u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
            && !db.SupervisorAssignments.Any(a => a.ProjectId == projectId && a.SupervisorProfile.UserId == u.Id)
            && !slots.Any(a => a.EvaluatorId == u.Id));
        if (component.Stage is "COLD" or "DEFENSE")
            query = query.Where(u => db.Set<DefenseCommitteeMember>().Any(m => m.UserId == u.Id
                && (component.Scope == "COMMON" || m.MajorId == component.MajorId)
                && db.Set<DefenseCommittee>().Any(c => c.Id == m.CommitteeId && c.ProjectId == projectId && c.Status == "CONFIRMED")));
        if (Weights(component) is { } configured)
        {
            var weightedIds = configured.Select(w => w.EvaluatorId).ToArray();
            query = query.Where(u => weightedIds.Contains(u.Id));
        }
        var count = await query.LongCountAsync(ct);
        var users = await query.OrderBy(u => u.FullName).ThenBy(u => u.Id).Skip((page-1)*pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.FullName, DepartmentId = u.DepartmentId!.Value, DepartmentName = u.Department!.Name }).ToListAsync(ct);
        return new(users.Select(u => new EligibleEvaluatorDto(u.Id, u.FullName, u.DepartmentId, u.DepartmentName,
            ["LECTURER", "COMMITTEE"])).ToArray(), page, pageSize, count);
    }
    private long Actor => current.UserId ?? throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task Manage(long projectId, CancellationToken ct)
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await evaluations.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (!actor.IsAdmin && !AcademicAuthority.HasScope(actor, project)) throw new ForbiddenException();
    }
    private async Task LeadAuthority(long projectId, CancellationToken ct)
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await evaluations.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (!AcademicAuthority.IsLead(actor, project))
            throw new ForbiddenException("The frozen lead department manages this project's evaluation scheme.");
        if (project.Status is "COMPLETED" or "ARCHIVED") throw new ConflictException("Completed and archived projects are read-only.");
    }
    private static bool IsPol03IndividualProcess(string? scope, string? stage) =>
        string.Equals(scope, "INDIVIDUAL", StringComparison.OrdinalIgnoreCase)
        && string.Equals(stage, "ON_GOING", StringComparison.OrdinalIgnoreCase);

    private async Task ComponentAuthority(long? rubricId, long projectId, CancellationToken ct, bool checkAuthority = true, string? scope = null, string? stage = null, long? majorId = null)
    {
        if (!rubricId.HasValue)
        {
            if (IsPol03IndividualProcess(scope, stage))
            {
                if (checkAuthority && majorId.HasValue)
                {
                    var actorCheck = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
                    var projCheck = (await evaluations.GetProjectAsync(projectId, ct))!;
                    var majorDept = await db.Majors.AsNoTracking().Where(m => m.Id == majorId.Value).Select(m => (long?)m.DepartmentId).SingleOrDefaultAsync(ct);
                    if (majorDept.HasValue && !AcademicAuthority.ManagesComponent(actorCheck, projCheck, majorDept.Value))
                        throw new ForbiddenException("Only the lead or responsible component department may manage this component.");
                }
                return;
            }
            throw new ConflictException("Each non-process component requires a published rubric.");
        }
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var rubric = await db.Rubrics.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rubricId.Value, ct)
            ?? throw new NotFoundException("Rubric", rubricId.Value);
        var project = (await evaluations.GetProjectAsync(projectId, ct))!;
        if (!rubric.DepartmentId.HasValue || !project.DepartmentIds.Contains(rubric.DepartmentId.Value)
            || rubric.AcademicSemesterId != project.SemesterId) throw new ConflictException("Rubric outside project academic scope.");
        if (checkAuthority && !AcademicAuthority.ManagesComponent(actor, project, rubric.DepartmentId.Value))
            throw new ForbiddenException("Only the lead or responsible component department may manage this component.");
        if (!await db.Set<RubricVersion>().AnyAsync(x => x.RubricId == rubricId.Value && x.Status == "PUBLISHED", ct))
            throw new ConflictException("Each component requires a published rubric.");
    }
    private async Task<EvaluationScheme> Row(long id, CancellationToken ct) =>
        await db.Set<EvaluationScheme>().Include(x => x.Components).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("EvaluationScheme", id);
    private async Task<EvaluationScheme> LockedRow(long id, CancellationToken ct)
    {
        var projectId = await db.Set<EvaluationScheme>().Where(x => x.Id == id).Select(x => (long?)x.ProjectId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("EvaluationScheme", id);
        await evaluations.LockProjectAsync(projectId, ct);
        await LeadAuthority(projectId, ct);
        return await Row(id, ct);
    }
    private static void Token(EvaluationScheme row, string? input)
    {
        if (!Guid.TryParse(input, out var token) || token != row.ConcurrencyToken) throw new ConflictException("Scheme changed. Reload before saving.");
    }
    private static EvaluationSchemeDto Map(EvaluationScheme r) => new(r.Id, r.RootId ?? r.Id, r.Version, r.ProjectId, r.ProjectPeriodId,
        r.Name, r.Status, r.PassThreshold, r.ConcurrencyToken.ToString("N"), r.PolicyVersionId,
        r.Components.OrderBy(c => c.Id).Select(c => new SchemeComponentDto(c.Id, c.Name, c.Scope, c.MajorId, c.RubricId,
            c.ProjectWeightPercent, c.StudentWeightPercent, c.RequiredEvaluators, Weights(c), c.Stage, c.TargetScope, c.IsIntegration)).ToArray(),
        JsonSerializer.Deserialize<SchemeStudent[]>(r.StudentsJson)!, r.CalculationRule, Policy(r), r.LockedAt);
    private static SchemePolicyInput? Policy(EvaluationScheme row) => row.GovernanceJson is null ? null
        : JsonSerializer.Deserialize<SchemePolicyInput>(row.GovernanceJson);
    private static IReadOnlyList<SchemeEvaluatorWeight>? Weights(EvaluationSchemeComponent row) => row.EvaluatorWeightsJson is null ? null
        : JsonSerializer.Deserialize<SchemeEvaluatorWeight[]>(row.EvaluatorWeightsJson);
    private async Task<EvaluationSchemeDto[]> EnrichSchemesAsync(IReadOnlyList<EvaluationSchemeDto> dtos, CancellationToken ct)
    {
        var evaluatorIds = dtos.SelectMany(d => d.Components)
            .SelectMany(c => c.EvaluatorWeights ?? [])
            .Select(w => w.EvaluatorId).Distinct().ToArray();
        if (evaluatorIds.Length == 0) return dtos.ToArray();
        var names = await db.Users.AsNoTracking().Where(u => evaluatorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return dtos.Select(d => d with
        {
            Components = d.Components.Select(c => c.EvaluatorWeights is null ? c : c with
            {
                EvaluatorWeights = c.EvaluatorWeights.Select(w => w with { EvaluatorName = names.GetValueOrDefault(w.EvaluatorId) }).ToArray()
            }).ToArray()
        }).ToArray();
    }
    private async Task<EvaluationSchemeDto> EnrichSchemeAsync(EvaluationSchemeDto dto, CancellationToken ct) =>
        (await EnrichSchemesAsync([dto], ct))[0];
    private async Task<EvaluationSchemeDto> Visible(EvaluationScheme row, CancellationToken ct)
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await evaluations.GetProjectAsync(row.ProjectId, ct) ?? throw new NotFoundException("Project", row.ProjectId);
        var dto = Map(row);
        if (actor.IsAdmin || AcademicAuthority.IsLead(actor, project)) return await EnrichSchemeAsync(dto, ct);
        if (!AcademicAuthority.HasScope(actor, project)) throw new ForbiddenException();
        var rubricIds = row.Components.Where(c => c.RubricId.HasValue).Select(c => c.RubricId!.Value).ToArray();
        var ownRubrics = await db.Rubrics.Where(r => rubricIds.Contains(r.Id) && r.DepartmentId == actor.DepartmentId)
            .Select(r => r.Id).ToArrayAsync(ct);
        var ownMajors = await db.Majors.AsNoTracking().Where(m => m.DepartmentId == actor.DepartmentId).Select(m => m.Id).ToArrayAsync(ct);
        return await EnrichSchemeAsync(dto with { Components = dto.Components.Where(c =>
            (c.RubricId.HasValue && ownRubrics.Contains(c.RubricId.Value))
            || (!c.RubricId.HasValue && c.MajorId.HasValue && ownMajors.Contains(c.MajorId.Value))).ToArray(),
            Students = dto.Students.Where(s => s.DepartmentId == actor.DepartmentId).ToArray() }, ct);
    }
    private Task Audit(string action, string type, long id, object details, CancellationToken ct) =>
        audit.RecordAsync(new(Actor, action, type, id, new Dictionary<string, object?> { ["snapshot"] = details }), ct);
    private async Task<RegistrationEvidence> Registration(long projectId, CancellationToken ct)
    {
        var snapshot = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct) ?? throw new ConflictException("A frozen registration snapshot is required.");
        return ProjectAcademicScopeReader.ParseEvidence(snapshot.SnapshotJson)
            ?? throw new ConflictException("Frozen registration scope is unknown. Legacy schemes remain read-only.");
    }
    private async Task Validate(EvaluationScheme row, CancellationToken ct)
    {
        var project = (await evaluations.GetProjectAsync(row.ProjectId, ct))!;
        if (project.Status != "FINAL_SUBMISSION" || !await evaluations.HasLockedSubmissionAsync(row.ProjectId, ct))
            throw new ConflictException("Configure schemes for a project with a locked final submission.");
        var period = await evaluations.GetPeriodAsync(row.ProjectPeriodId, Now, ct);
        if (period is null || period.SemesterId != project.SemesterId || !period.IsOpen) throw new ConflictException("Evaluation period is closed.");
        var registration = await Registration(row.ProjectId, ct);
        var majors = registration.Scope.Requirements.Select(x => x.MajorId).Distinct().ToArray();
        EvaluationSchemeRules.Validate(row.Components.Select(c => new SchemeComponentInput(c.Name, c.Scope, c.MajorId, c.RubricId,
            c.ProjectWeightPercent, c.StudentWeightPercent, c.RequiredEvaluators, Weights(c), c.Stage ?? "DEFENSE", c.TargetScope, c.IsIntegration)).ToArray(), majors, row.PassThreshold);
        var policy = Policy(row) ?? throw new ConflictException("Scheme approval/calculation provenance is unknown; create an explicit new version.");
        VersionedResultCalculator.Validate(policy, row.Components.Select(c => new SchemeComponentInput(c.Name, c.Scope, c.MajorId,
            c.RubricId, c.ProjectWeightPercent, c.StudentWeightPercent, c.RequiredEvaluators, Weights(c), c.Stage ?? "DEFENSE", c.TargetScope, c.IsIntegration)).ToArray(), majors);
        foreach (var component in row.Components)
        {
            await ComponentAuthority(component.RubricId, row.ProjectId, ct, checkAuthority: false, component.Scope, component.Stage, component.MajorId);
            if (component.MajorId.HasValue && component.RubricId.HasValue && !await db.Majors.AnyAsync(m => m.Id == component.MajorId
                && db.Rubrics.Any(r => r.Id == component.RubricId.Value && r.DepartmentId == m.DepartmentId), ct))
                throw new ConflictException("Major-specific rubric must belong to the major department.");
        }
        var departments = await db.Majors.Where(m => majors.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.DepartmentId, ct);
        var students = registration.Members.OrderBy(x => x.UserId).Select(x => new SchemeStudent(x.UserId, x.MajorId,
            registration.MajorDepartmentIds?.GetValueOrDefault(x.MajorId) ?? departments.GetValueOrDefault(x.MajorId))).ToArray();
        if (students.Length == 0 || students.Select(s => s.StudentId).Distinct().Count() != students.Length
            || students.Any(s => !majors.Contains(s.MajorId))) throw new ConflictException("Invalid frozen student roster.");
        foreach (var student in students)
            if (!await db.TeamMembers.AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == row.ProjectId && m.UserId == student.StudentId
                && m.LeftAt == null && m.User.Status == "ACTIVE" && m.User.AcademicProfileStatus == "VERIFIED"
                && m.User.MajorId == student.MajorId && m.User.DepartmentId == student.DepartmentId
                && m.User.Major != null && m.User.Major.IsActive && m.User.Major.DepartmentId == student.DepartmentId
                && m.User.Major.Department.IsActive && m.User.Major.Department.Organization.IsActive
                && m.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct))
                throw new ConflictException("Frozen roster must match active verified students before scheme publication.");
        row.StudentsJson = JsonSerializer.Serialize(students); row.RegistrationSnapshotJson = JsonSerializer.Serialize(registration);
    }
    public Task<EvaluationSchemeDto> SaveAsync(long? id, SaveEvaluationSchemeRequest input, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        await evaluations.LockProjectAsync(input.ProjectId, ct); await LeadAuthority(input.ProjectId, ct);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200 || input.Components is null
            || input.Components.Any(x => x is null || x.Stage is not ("ON_GOING" or "COLD" or "DEFENSE")))
            throw new ValidationException(new Dictionary<string, string[]> { ["scheme"] = ["A name and components with supported stages are required."] });
        if (input.Policy is not null && !input.Policy.IsDemo && (input.Policy.ApprovalStatus != "APPROVED" || string.IsNullOrWhiteSpace(input.Policy.ApprovalReference)))
            throw new AIPMS.Domain.Exceptions.DomainException("Official approved schemes require a non-empty Academic Council or Department approval reference.", "SCHEME_APPROVAL_REFERENCE_REQUIRED");
        var row = id.HasValue ? await Row(id.Value, ct) : new EvaluationScheme { ProjectId = input.ProjectId,
            Version = (await db.Set<EvaluationScheme>().Where(x => x.ProjectId == input.ProjectId).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1,
            CreatedBy = Actor, CreatedAt = Now };
        if (row.ProjectId != input.ProjectId) throw new NotFoundException("EvaluationScheme", id!);
        if (id.HasValue) Token(row, input.ConcurrencyToken);
        if (row.Status != "DRAFT") throw new ConflictException("Published schemes are immutable; create a new version.");
        foreach (var c in row.Components) await ComponentAuthority(c.RubricId, row.ProjectId, ct, scope: c.Scope, stage: c.Stage, majorId: c.MajorId);
        db.RemoveRange(row.Components); row.Components.Clear();
        row.Name = input.Name.Trim(); row.ProjectPeriodId = input.ProjectPeriodId; row.PassThreshold = input.PassThreshold;
        if (input.Policy is not null || !id.HasValue)
        {
            var policy = input.Policy ?? new SchemePolicyInput();
            row.GovernanceJson = JsonSerializer.Serialize(policy);
            row.CalculationRule = VersionedResultCalculator.CalculationRuleFor(policy);
        }
        row.ConcurrencyToken = Guid.NewGuid();
        row.Components = input.Components.Select(c => new EvaluationSchemeComponent { Name = c.Name, Scope = c.Scope, MajorId = c.MajorId,
            RubricId = c.RubricId, ProjectWeightPercent = c.ProjectWeightPercent, StudentWeightPercent = c.StudentWeightPercent,
            RequiredEvaluators = c.RequiredEvaluators, Stage = c.Stage, TargetScope = c.TargetScope, IsIntegration = c.IsIntegration,
            EvaluatorWeightsJson = c.EvaluatorWeights is null ? null : JsonSerializer.Serialize(c.EvaluatorWeights
                .Select(w => w is null ? null : new SchemeEvaluatorWeight(w.EvaluatorId, w.WeightPercent)).OrderBy(w => w?.EvaluatorId)) }).ToList();
        await Validate(row, ct);
        if (!id.HasValue) db.Add(row);
        await db.SaveChangesAsync(ct); await Audit("EVALUATION_SCHEME_SAVED", "EVALUATION_SCHEME", row.Id, Map(row), ct); return await EnrichSchemeAsync(Map(row), ct);
    }, ct);
    public Task<EvaluationSchemeDto> GetAsync(long id, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    { var row = await Row(id, ct); await Manage(row.ProjectId, ct); return await Visible(row, ct); }, ct);
    public Task<EvaluationSchemeDto> UpdateComponentAsync(long schemeId, long componentId, UpdateSchemeComponentRequest input, CancellationToken ct) =>
        evaluations.InTransactionAsync(async () =>
        {
            var projectId = await db.Set<EvaluationScheme>().Where(s => s.Id == schemeId).Select(s => (long?)s.ProjectId).SingleOrDefaultAsync(ct)
                ?? throw new NotFoundException("EvaluationScheme", schemeId);
            await evaluations.LockProjectAsync(projectId, ct);
            var row = await Row(schemeId, ct);
            var component = row.Components.SingleOrDefault(c => c.Id == componentId) ?? throw new NotFoundException("SchemeComponent", componentId);
            await ComponentAuthority(component.RubricId, projectId, ct, scope: component.Scope, stage: component.Stage, majorId: component.MajorId);
            Token(row, input.ConcurrencyToken);
            if (row.Status != "DRAFT") throw new ConflictException("Published scheme components are immutable.");
            var change = input.Component ?? throw new ValidationException(new Dictionary<string, string[]> { ["component"] = ["A component is required."] });
            if (change.Stage is not ("ON_GOING" or "COLD" or "DEFENSE"))
                throw new ValidationException(new Dictionary<string, string[]> { ["stage"] = ["Specify ON_GOING, COLD or DEFENSE."] });
            if (change.Scope != component.Scope || change.MajorId != component.MajorId)
                throw new ConflictException("Component targets are changed through the lead department scheme workflow.");
            await ComponentAuthority(change.RubricId, projectId, ct, scope: change.Scope, stage: change.Stage, majorId: change.MajorId);
            component.Name = change.Name;
            component.RubricId = change.RubricId;
            component.ProjectWeightPercent = change.ProjectWeightPercent;
            component.StudentWeightPercent = change.StudentWeightPercent;
            component.RequiredEvaluators = change.RequiredEvaluators;
            component.Stage = change.Stage;
            component.TargetScope = change.TargetScope;
            component.IsIntegration = change.IsIntegration;
            component.EvaluatorWeightsJson = change.EvaluatorWeights is null ? null : JsonSerializer.Serialize(change.EvaluatorWeights
                .Select(w => w is null ? null : new SchemeEvaluatorWeight(w.EvaluatorId, w.WeightPercent)).OrderBy(w => w?.EvaluatorId));
            await Validate(row, ct);
            row.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await Audit("EVALUATION_SCHEME_COMPONENT_UPDATED", "EVALUATION_SCHEME", row.Id, new { componentId, Scheme = Map(row) }, ct);
            return await Visible(row, ct);
        }, ct);
    public Task<IReadOnlyList<EvaluationSchemeDto>> ListAsync(long projectId, CancellationToken ct) => evaluations.InTransactionAsync<IReadOnlyList<EvaluationSchemeDto>>(async () =>
    {
        await Manage(projectId, ct);
        var rows = await db.Set<EvaluationScheme>().Include(x => x.Components).Where(x => x.ProjectId == projectId).OrderByDescending(x => x.Version).ToListAsync(ct);
        var actor = (await evaluations.GetActorAsync(Actor, ct))!;
        var project = (await evaluations.GetProjectAsync(projectId, ct))!;
        if (actor.IsAdmin || AcademicAuthority.IsLead(actor, project)) return await EnrichSchemesAsync(rows.Select(Map).ToArray(), ct);
        var rubricIds = rows.SelectMany(r => r.Components.Where(c => c.RubricId.HasValue).Select(c => c.RubricId!.Value)).Distinct().ToArray();
        var ownRubrics = (await db.Rubrics.Where(r => rubricIds.Contains(r.Id) && r.DepartmentId == actor.DepartmentId)
            .Select(r => r.Id).ToArrayAsync(ct)).ToHashSet();
        var ownMajors = (await db.Majors.AsNoTracking().Where(m => m.DepartmentId == actor.DepartmentId)
            .Select(m => m.Id).ToArrayAsync(ct)).ToHashSet();
        var filtered = rows.Select(r => Map(r)).Select(r => r with {
            Components = r.Components.Where(c =>
                (c.RubricId.HasValue && ownRubrics.Contains(c.RubricId.Value))
                || (!c.RubricId.HasValue && c.MajorId.HasValue && ownMajors.Contains(c.MajorId.Value))).ToArray(),
            Students = r.Students.Where(s => s.DepartmentId == actor.DepartmentId).ToArray() }).ToArray();
        return await EnrichSchemesAsync(filtered, ct);
    }, ct);
    public Task<EvaluationSchemeDto> PublishAsync(long id, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var row = await LockedRow(id, ct); Token(row, token);
        if (row.Status != "DRAFT") throw new ConflictException("Scheme is already published.");
        await Validate(row, ct);
        var old = await db.Set<EvaluationScheme>().SingleOrDefaultAsync(x => x.ProjectId == row.ProjectId && x.Status == "PUBLISHED", ct);
        if (old is not null)
        {
            var hasActiveOrEvaluated = await db.Set<EvaluationAssignment>().AnyAsync(a => a.ProjectId == row.ProjectId
                && a.ComponentId != null
                && (a.Status == "ACTIVE" || db.Set<EvaluationDraftState>().Any(s => s.AssignmentId == a.Id)), ct);
            if (hasActiveOrEvaluated)
                throw new ConflictException("A project already using a scheme cannot replace its scoring history.");
            old.Status = "RETIRED"; await db.SaveChangesAsync(ct);
        }
        row.PolicyVersionId = await PolicyVersions.CaptureAsync(db, row.ProjectPeriodId, "EVALUATION_SCHEME", row.Id, Now, ct);
        row.Status = "PUBLISHED"; row.PublishedBy = Actor; row.PublishedAt = Now; row.LockedAt = Now; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Audit("EVALUATION_SCHEME_PUBLISHED", "EVALUATION_SCHEME", id, Map(row), ct); return await EnrichSchemeAsync(Map(row), ct);
    }, ct);
    public Task<EvaluationSchemeDto> VersionAsync(long id, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var source = await LockedRow(id, ct); Token(source, token);
        if (source.Status == "DRAFT") throw new ConflictException("Edit the draft directly.");
        foreach (var c in source.Components) await ComponentAuthority(c.RubricId, source.ProjectId, ct, scope: c.Scope, stage: c.Stage, majorId: c.MajorId);
        var row = new EvaluationScheme { RootId = source.RootId ?? source.Id, ProjectId = source.ProjectId, ProjectPeriodId = source.ProjectPeriodId,
            Version = await db.Set<EvaluationScheme>().Where(x => x.ProjectId == source.ProjectId).MaxAsync(x => x.Version, ct) + 1,
            Name = source.Name, PassThreshold = source.PassThreshold,
            CalculationRule = VersionedResultCalculator.CalculationRuleFor(Policy(source)),
            GovernanceJson = JsonSerializer.Serialize((Policy(source) ?? new SchemePolicyInput()) with {
                IsDemo = true, ApprovalStatus = "DEMO", ApprovalReference = null }),
            CreatedBy = Actor, CreatedAt = Now, ConcurrencyToken = Guid.NewGuid(),
            StudentsJson = source.StudentsJson, RegistrationSnapshotJson = source.RegistrationSnapshotJson,
            Components = source.Components.Select(c => new EvaluationSchemeComponent { Name = c.Name, Scope = c.Scope, MajorId = c.MajorId,
                RubricId = c.RubricId, ProjectWeightPercent = c.ProjectWeightPercent, StudentWeightPercent = c.StudentWeightPercent,
                RequiredEvaluators = c.RequiredEvaluators, Stage = c.Stage, TargetScope = c.TargetScope, IsIntegration = c.IsIntegration,
                EvaluatorWeightsJson = c.EvaluatorWeightsJson }).ToList() };
        db.Add(row); await db.SaveChangesAsync(ct); await Audit("EVALUATION_SCHEME_VERSIONED", "EVALUATION_SCHEME", row.Id, Map(row), ct); return await EnrichSchemeAsync(Map(row), ct);
    }, ct);
    public async Task DeleteAsync(long id, string token, CancellationToken ct) => await evaluations.InTransactionAsync(async () =>
    {
        var row = await LockedRow(id, ct); Token(row, token);
        if (row.Status != "DRAFT") throw new ConflictException("Only drafts can be deleted.");
        foreach (var c in row.Components) await ComponentAuthority(c.RubricId, row.ProjectId, ct, scope: c.Scope, stage: c.Stage, majorId: c.MajorId);
        db.Remove(row); await db.SaveChangesAsync(ct); await Audit("EVALUATION_SCHEME_DELETED", "EVALUATION_SCHEME", id, Map(row), ct); return true;
    }, ct);
    public async Task EnsureCommitteeMemberForReplacementAsync(long projectId, long replacementEvaluatorId, string scope, long? majorId, CancellationToken ct)
    {
        var committee = await db.Set<DefenseCommittee>()
            .Include(c => c.Members)
            .SingleOrDefaultAsync(c => c.ProjectId == projectId && c.Status == "CONFIRMED", ct)
            ?? throw new ConflictException("A confirmed defense committee is required.", WorkflowErrorCodes.DefenseCommitteeRequired);

        var targetMajorId = majorId;
        if (!targetMajorId.HasValue)
        {
            var evaluatorDeptId = await db.Users.AsNoTracking()
                .Where(u => u.Id == replacementEvaluatorId)
                .Select(u => u.DepartmentId)
                .SingleOrDefaultAsync(ct);
            targetMajorId = await db.ProjectMajors.AsNoTracking()
                .Where(pm => pm.ProjectId == projectId && (!evaluatorDeptId.HasValue || pm.Major.DepartmentId == evaluatorDeptId.Value))
                .Select(pm => (long?)pm.MajorId)
                .FirstOrDefaultAsync(ct)
                ?? await db.ProjectMajors.AsNoTracking()
                    .Where(pm => pm.ProjectId == projectId)
                    .Select(pm => (long?)pm.MajorId)
                    .FirstOrDefaultAsync(ct);
        }

        if (!targetMajorId.HasValue)
            throw new ConflictException("Project major could not be resolved for committee member replacement.");

        var alreadyOnCommittee = committee.Members.Any(m => m.UserId == replacementEvaluatorId
            && (scope == "COMMON" || m.MajorId == targetMajorId.Value));
        if (!alreadyOnCommittee)
        {
            committee.Members.Add(new DefenseCommitteeMember
            {
                CommitteeId = committee.Id,
                UserId = replacementEvaluatorId,
                MajorId = targetMajorId.Value,
                Role = "MEMBER"
            });
            committee.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<BulkApplyEvaluationSchemeResultDto> BulkApplyByTrackAsync(BulkApplyEvaluationSchemeRequest input, CancellationToken ct)
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        if (actor.IsAdmin || !actor.IsStaff || !actor.DepartmentId.HasValue)
            throw new ForbiddenException("Only active Lead Department Staff can bulk-apply evaluation schemes by track.", WorkflowErrorCodes.AdminNotAcademicAuthority);
        if (string.IsNullOrWhiteSpace(input.TrackCode))
            throw new ValidationException(new Dictionary<string, string[]> { ["trackCode"] = ["TrackCode is required."] });

        var track = input.TrackCode.Trim().ToUpperInvariant();
        var period = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.Id == input.ProjectPeriodId)
            .Select(p => new { p.Id, p.AcademicSemesterId, p.PeriodType, p.Status })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectPeriod", input.ProjectPeriodId);
        if (!string.Equals(period.PeriodType, "EVALUATION", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Bulk evaluation scheme application requires an EVALUATION project period.");

        string? resolvedName = input.Name;
        decimal? resolvedPass = input.PassThreshold;
        SchemePolicyInput? resolvedPolicy = input.Policy;
        IReadOnlyList<SchemeComponentInput>? resolvedComponents = input.Components;

        if (input.SourceBlueprintId.HasValue || (input.UsePublishedBlueprint && (resolvedComponents is null || resolvedComponents.Count == 0)))
        {
            var bpQuery = db.Set<AcademicBlueprint>().AsNoTracking()
                .Where(b => b.AcademicSemesterId == period.AcademicSemesterId
                    && b.TrackCode == track
                    && b.LeadDepartmentId == actor.DepartmentId.Value
                    && b.Status == "PUBLISHED");
            var blueprint = input.SourceBlueprintId.HasValue
                ? await bpQuery.SingleOrDefaultAsync(b => b.Id == input.SourceBlueprintId.Value, ct)
                : await bpQuery.OrderByDescending(b => b.Version).FirstOrDefaultAsync(ct);
            if (blueprint is null)
                throw new NotFoundException("AcademicBlueprint", input.SourceBlueprintId ?? 0);
            var cfg = JsonSerializer.Deserialize<AIPMS.Application.Features.AcademicBlueprints.AcademicBlueprintConfig>(blueprint.ConfigJson);
            if (cfg?.EvaluationSchemeTemplate is not null)
            {
                resolvedName ??= cfg.EvaluationSchemeTemplate.Name;
                resolvedPass ??= cfg.EvaluationSchemeTemplate.PassThreshold;
                resolvedPolicy ??= cfg.EvaluationSchemeTemplate.Policy;
                if (resolvedComponents is null || resolvedComponents.Count == 0)
                    resolvedComponents = cfg.EvaluationSchemeTemplate.Components;
            }
        }
        else if (input.SourceSchemeId.HasValue && (resolvedComponents is null || resolvedComponents.Count == 0))
        {
            var src = await db.Set<EvaluationScheme>().AsNoTracking()
                .Include(s => s.Components)
                .SingleOrDefaultAsync(s => s.Id == input.SourceSchemeId.Value, ct)
                ?? throw new NotFoundException("EvaluationScheme", input.SourceSchemeId.Value);
            var srcProject = await evaluations.GetProjectAsync(src.ProjectId, ct);
            if (srcProject is null || !AcademicAuthority.IsLead(actor, srcProject))
                throw new ForbiddenException("Source scheme must belong to a project led by your department.");
            resolvedName ??= src.Name;
            resolvedPass ??= src.PassThreshold;
            resolvedPolicy ??= Policy(src);
            resolvedComponents = src.Components.OrderBy(c => c.Id).Select(c => new SchemeComponentInput(
                c.Name, c.Scope, c.MajorId, c.RubricId, c.ProjectWeightPercent, c.StudentWeightPercent,
                c.RequiredEvaluators, Weights(c), c.Stage ?? "DEFENSE", c.TargetScope, c.IsIntegration)).ToArray();
        }

        if (string.IsNullOrWhiteSpace(resolvedName) || resolvedComponents is null || resolvedComponents.Count == 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["components"] = ["Scheme name and components (or a published blueprint/source scheme template) are required."] });

        var effectivePass = resolvedPass ?? 5.00m;
        var candidateProjects = await db.Projects.AsNoTracking()
            .Where(p => p.Team.AcademicSemesterId == period.AcademicSemesterId
                && p.TrackCode == track
                && p.Status == "FINAL_SUBMISSION"
                && db.Set<ProjectRegistrationSnapshot>().Any(s => s.ProjectId == p.Id && s.LeadDepartmentId == actor.DepartmentId.Value))
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Code })
            .ToArrayAsync(ct);

        var appliedIds = new List<long>();
        var outcomes = new List<BulkApplyProjectOutcomeDto>();

        foreach (var proj in candidateProjects)
        {
            if (await db.Set<EvaluationScheme>().AsNoTracking().AnyAsync(s => s.ProjectId == proj.Id && s.Status == "PUBLISHED", ct))
            {
                outcomes.Add(new BulkApplyProjectOutcomeDto(proj.Id, proj.Code, "SKIPPED", null, "ALREADY_HAS_PUBLISHED_SCHEME"));
                continue;
            }
            if (!await evaluations.HasLockedSubmissionAsync(proj.Id, ct))
            {
                outcomes.Add(new BulkApplyProjectOutcomeDto(proj.Id, proj.Code, "SKIPPED", null, "LOCKED_PACKAGE_REQUIRED"));
                continue;
            }

            try
            {
                var saveReq = new SaveEvaluationSchemeRequest(
                    proj.Id,
                    input.ProjectPeriodId,
                    resolvedName,
                    effectivePass,
                    resolvedComponents,
                    null,
                    resolvedPolicy);
                var saved = await SaveAsync(null, saveReq, ct);
                if (input.AutoPublish)
                    saved = await PublishAsync(saved.Id, saved.ConcurrencyToken, ct);
                appliedIds.Add(proj.Id);
                outcomes.Add(new BulkApplyProjectOutcomeDto(proj.Id, proj.Code, "APPLIED", saved.Id, null));
            }
            catch (Exception ex) when (ex is ConflictException or ValidationException or AIPMS.Domain.Exceptions.DomainException or ForbiddenException or NotFoundException)
            {
                outcomes.Add(new BulkApplyProjectOutcomeDto(proj.Id, proj.Code, "FAILED", null, ex.Message));
            }
        }

        await Audit("EVALUATION_SCHEME_BULK_APPLIED", "PROJECT_PERIOD", input.ProjectPeriodId, new
        {
            TrackCode = track,
            input.AutoPublish,
            AppliedCount = appliedIds.Count,
            SkippedCount = outcomes.Count(o => o.Status == "SKIPPED"),
            FailedCount = outcomes.Count(o => o.Status == "FAILED"),
            AppliedProjectIds = appliedIds
        }, ct);

        return new BulkApplyEvaluationSchemeResultDto(
            appliedIds.Count,
            outcomes.Count(o => o.Status == "SKIPPED"),
            outcomes.Count(o => o.Status == "FAILED"),
            appliedIds,
            outcomes);
    }

    public async Task<ScopedAssignmentContext> ResolveAssignmentAsync(long projectId, AssignEvaluatorRequest input, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Assignment scope needs the project transaction.");
        if (input.ComponentId is null || input.Scope is null) throw new ConflictException("Specify a published scheme component and explicit scope; legacy assignments are read-only.");
        var component = await db.Set<EvaluationSchemeComponent>().SingleOrDefaultAsync(c => c.Id == input.ComponentId, ct)
            ?? throw new NotFoundException("SchemeComponent", input.ComponentId);
        var scheme = await Row(component.SchemeId, ct);
        if (IsPol03IndividualProcess(component.Scope, component.Stage)
            && (scheme.CalculationRule == VersionedResultCalculator.PolV4Version || !component.RubricId.HasValue))
            throw new ConflictException(
                "The 20% individual process component (POL-03) is graded directly by the Primary Supervisor across Gates G1–G3 and cannot be assigned to external evaluators.",
                WorkflowErrorCodes.IndividualProcessManagedBySupervisor);
        if (scheme.ProjectId != projectId || scheme.ProjectPeriodId != input.ProjectPeriodId) throw new NotFoundException("SchemeComponent", input.ComponentId);
        if (scheme.Status != "PUBLISHED" || scheme.PolicyVersionId is null) throw new ConflictException("Publish the scheme before assigning evaluators.");
        if (Weights(component) is { } weighted && weighted.All(w => w.EvaluatorId != input.EvaluatorId))
            throw new ConflictException("This evaluator is not a weighted slot in the locked scheme version.");
        if (input.Scope != component.Scope || input.MajorId != component.MajorId || (component.Scope == "INDIVIDUAL") != input.StudentId.HasValue)
            throw new ConflictException("Assignment scope and targets must match the published component.");
        if (input.StudentId is long studentId)
        {
            if (!JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson)!.Any(s => s.StudentId == studentId && s.MajorId == component.MajorId)
                || !await db.TeamMembers.AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.UserId == studentId && m.LeftAt == null
                    && m.User.Status == "ACTIVE" && m.User.AcademicProfileStatus == "VERIFIED"
                    && m.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct)) throw new ConflictException("Student is outside the frozen eligible project roster.");
        }
        var assignments = db.Set<EvaluationAssignment>().Where(a => a.ProjectId == projectId && a.ComponentId == component.Id
            && a.StudentId == input.StudentId && a.Status == "ACTIVE");
        if (await assignments.AnyAsync(a => a.EvaluatorId == input.EvaluatorId, ct) || await assignments.CountAsync(ct) >= component.RequiredEvaluators)
            throw new ConflictException("The evaluator already occupies this slot or the component is fully assigned.");
        var committee = component.Stage is "COLD" or "DEFENSE"
            ? await DefenseCommitteeAuthority.RequireAsync(db, projectId, input.EvaluatorId, component.Scope, component.MajorId, ct) : null;
        var cold = component.Stage == "COLD" ? await ColdEvaluationBinding.Capture(db, projectId, component.Scope, component.MajorId, input.StudentId, ct) : null;
        var packageId = await db.Set<FinalSubmission>().Where(s => s.ProjectId == projectId).Select(s => s.Id).SingleAsync(ct);
        var resolvedRubricId = component.RubricId ?? throw new ConflictException("Component requires a published rubric.");
        return new(component.Id, scheme.Id, component.Scope, component.MajorId, input.StudentId, resolvedRubricId,
            scheme.PolicyVersionId.Value, JsonSerializer.Serialize(new { Scheme = Map(scheme), component, input.StudentId,
                FinalSubmissionId = packageId, EvaluatorSource = input.EvaluationType, Committee = committee, Cold = cold }), component.Stage, cold?.PackageId);
    }
    public async Task EnsureWritableAsync(EvaluationAssignmentRecord assignment, CancellationToken ct)
    {
        if (assignment.Scope == "UNKNOWN" || assignment.ComponentId is null || assignment.PolicyVersionId is null)
            throw new ConflictException("Legacy evaluations without proven scope are read-only. Create a scoped assignment.");
        var component = await db.Set<EvaluationSchemeComponent>().SingleAsync(c => c.Id == assignment.ComponentId, ct);
        var scheme = await Row(component.SchemeId, ct);
        if (scheme.Status != "PUBLISHED" || scheme.ProjectId != assignment.ProjectId || scheme.PolicyVersionId != assignment.PolicyVersionId
            || scheme.ProjectPeriodId != assignment.PeriodId
            || component.Stage != assignment.Stage || component.Scope != assignment.Scope || component.MajorId != assignment.MajorId || component.RubricId != assignment.RubricId)
            throw new ConflictException("Assignment does not match its frozen scheme.");
        if (assignment.Scope == "INDIVIDUAL" && (!assignment.StudentId.HasValue
            || !JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson)!.Any(s => s.StudentId == assignment.StudentId && s.MajorId == assignment.MajorId)))
            throw new ConflictException("Individual assignment is outside the frozen roster.");
        if (assignment.Stage is "COLD" or "DEFENSE") await DefenseCommitteeAuthority.RequirePinnedAsync(db,
            await db.Set<EvaluationAssignment>().AsNoTracking().SingleAsync(a => a.Id == assignment.Id, ct), ct);
        if (assignment.Stage == "DEFENSE")
        {
            var readiness = await cold.ProgressCore(assignment.ProjectId, ct, system: true);
            if (!readiness.Ready) throw new ConflictException("Complete COLD before defense grading: " + string.Join(", ", readiness.Blockers), "COLD_NOT_READY");
            // POL-06 (AGENTS.md §1.3): Defense room scheduling is optional (non-blocking) unless explicitly enabled on the period.
            var requireDefenseRoom = await db.ProjectPeriods.AsNoTracking()
                .Where(p => p.Id == assignment.PeriodId)
                .Select(p => p.EnableDefenseRoomScheduling)
                .SingleOrDefaultAsync(ct);
            if (requireDefenseRoom && !await db.Set<DefenseSession>().AnyAsync(s => s.ProjectId == assignment.ProjectId && s.Status == "SCHEDULED"
                && db.Set<DefenseCommittee>().Any(c => c.Id == s.CommitteeId && c.Status == "CONFIRMED"), ct))
                throw new ConflictException("Schedule defense before grading its rubric.", "DEFENSE_SESSION_REQUIRED");
        }
        if (assignment.Stage == "COLD") await ColdEvaluationBinding.Validate(db,
            await db.Set<EvaluationAssignment>().AsNoTracking().SingleAsync(a => a.Id == assignment.Id, ct), ct);
    }

    public async Task<EvaluationEvidenceRecord> BindEvidenceAsync(EvaluationAssignmentRecord assignment, EvaluationEvidenceRecord finalEvidence, CancellationToken ct)
    {
        if (assignment.Stage != "COLD") return finalEvidence with { Stage = assignment.Stage };
        var bound = await ColdEvaluationBinding.Validate(db,
            await db.Set<EvaluationAssignment>().AsNoTracking().SingleAsync(a => a.Id == assignment.Id, ct), ct);
        return finalEvidence with { Stage = "COLD", ColdPackageId = bound.PackageId, ColdSubmissionIds = bound.SubmissionIds,
            ColdEvidenceHash = bound.Hash, ArtifactCount = bound.SubmissionIds.Count, FileCount = bound.FileCount,
            DeliverableVersionIds = [] };
    }
}
