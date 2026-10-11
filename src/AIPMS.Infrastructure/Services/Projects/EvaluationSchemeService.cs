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
    IAuditTrail audit, IPublisher publisher, TimeProvider clock) : IEvaluationSchemeService
{
    public async Task<AIPMS.Application.Common.Models.PagedResult<EligibleEvaluatorDto>> CandidatesAsync(long projectId, long periodId,
        long componentId, string? scope, long? majorId, long? studentId, int page, int pageSize, CancellationToken ct)
    {
        await Manage(projectId, ct);
        var component = await db.Set<EvaluationSchemeComponent>().SingleOrDefaultAsync(c => c.Id == componentId, ct)
            ?? throw new NotFoundException("SchemeComponent", componentId);
        var scheme = await Row(component.SchemeId, ct);
        if (scheme.ProjectId != projectId || scheme.ProjectPeriodId != periodId) throw new NotFoundException("SchemeComponent", componentId);
        if (scheme.Status != "PUBLISHED" || scope != component.Scope || majorId != component.MajorId
            || (scope == "INDIVIDUAL") != studentId.HasValue) throw new ConflictException("Select a published component and matching target.");
        if (studentId.HasValue && !JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson)!.Any(s => s.StudentId == studentId && s.MajorId == majorId))
            throw new NotFoundException("Student", studentId);
        await ComponentAuthority(component.RubricId, projectId, ct);
        var period = await evaluations.GetPeriodAsync(periodId, Now, ct);
        var project = (await evaluations.GetProjectAsync(projectId, ct))!;
        if (period is null || !period.IsOpen || period.SemesterId != project.SemesterId)
            throw new ConflictException("Evaluation period is closed.", WorkflowErrorCodes.EvaluationWindowClosed);
        if (project.Status != "FINAL_SUBMISSION" || !await evaluations.HasLockedSubmissionAsync(projectId, ct))
            throw new ConflictException("A locked final-submission package is required.", WorkflowErrorCodes.FinalPackageRequired);
        var department = await db.Rubrics.Where(r => r.Id == component.RubricId).Select(r => r.DepartmentId!.Value).SingleAsync(ct);
        var slots = db.Set<EvaluationAssignment>().Where(a => a.ComponentId == componentId && a.StudentId == studentId && a.Status == "ACTIVE");
        if (await slots.CountAsync(ct) >= component.RequiredEvaluators) return new([], page, pageSize, 0);
        var query = db.Users.AsNoTracking().Where(u => u.Status == "ACTIVE" && u.DepartmentId == department
            && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive && u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
            && !db.SupervisorAssignments.Any(a => a.ProjectId == projectId && a.IsPrimary && a.AssignmentType == "PRIMARY"
                && a.EndedAt == null && a.SupervisorProfile.UserId == u.Id)
            && !slots.Any(a => a.EvaluatorId == u.Id));
        var count = await query.LongCountAsync(ct);
        var users = await query.OrderBy(u => u.FullName).ThenBy(u => u.Id).Skip((page-1)*pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.FullName, DepartmentName = u.Department!.Name }).ToListAsync(ct);
        return new(users.Select(u => new EligibleEvaluatorDto(u.Id, u.FullName, department, u.DepartmentName,
            ["LECTURER"])).ToArray(), page, pageSize, count);
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
    private async Task ComponentAuthority(long rubricId, long projectId, CancellationToken ct, bool checkAuthority = true)
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var rubric = await db.Rubrics.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rubricId, ct)
            ?? throw new NotFoundException("Rubric", rubricId);
        var project = (await evaluations.GetProjectAsync(projectId, ct))!;
        if (!rubric.DepartmentId.HasValue || !project.DepartmentIds.Contains(rubric.DepartmentId.Value)
            || rubric.AcademicSemesterId != project.SemesterId) throw new ConflictException("Rubric outside project academic scope.");
        if (checkAuthority && !AcademicAuthority.ManagesComponent(actor, project, rubric.DepartmentId.Value))
            throw new ForbiddenException("Only the lead or responsible component department may manage this component.");
        if (!await db.Set<RubricVersion>().AnyAsync(x => x.RubricId == rubricId && x.Status == "PUBLISHED", ct))
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
            c.ProjectWeightPercent, c.StudentWeightPercent, c.RequiredEvaluators)).ToArray(),
        JsonSerializer.Deserialize<SchemeStudent[]>(r.StudentsJson)!, r.CalculationRule);
    private async Task<EvaluationSchemeDto> Visible(EvaluationScheme row, CancellationToken ct)
    {
        var actor = await evaluations.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await evaluations.GetProjectAsync(row.ProjectId, ct) ?? throw new NotFoundException("Project", row.ProjectId);
        var dto = Map(row);
        if (actor.IsAdmin || AcademicAuthority.IsLead(actor, project)) return dto;
        if (!AcademicAuthority.HasScope(actor, project)) throw new ForbiddenException();
        var rubricIds = row.Components.Select(c => c.RubricId).ToArray();
        var ownRubrics = await db.Rubrics.Where(r => rubricIds.Contains(r.Id) && r.DepartmentId == actor.DepartmentId)
            .Select(r => r.Id).ToArrayAsync(ct);
        return dto with { Components = dto.Components.Where(c => ownRubrics.Contains(c.RubricId)).ToArray(),
            Students = dto.Students.Where(s => s.DepartmentId == actor.DepartmentId).ToArray() };
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
            c.ProjectWeightPercent, c.StudentWeightPercent, c.RequiredEvaluators)).ToArray(), majors, row.PassThreshold);
        foreach (var component in row.Components)
        {
            await ComponentAuthority(component.RubricId, row.ProjectId, ct, checkAuthority: false);
            if (component.MajorId.HasValue && !await db.Majors.AnyAsync(m => m.Id == component.MajorId
                && db.Rubrics.Any(r => r.Id == component.RubricId && r.DepartmentId == m.DepartmentId), ct))
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
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200 || input.Components is null || input.Components.Any(x => x is null))
            throw new ValidationException(new Dictionary<string, string[]> { ["scheme"] = ["A name and components are required."] });
        var row = id.HasValue ? await Row(id.Value, ct) : new EvaluationScheme { ProjectId = input.ProjectId,
            Version = (await db.Set<EvaluationScheme>().Where(x => x.ProjectId == input.ProjectId).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1,
            CreatedBy = Actor, CreatedAt = Now };
        if (row.ProjectId != input.ProjectId) throw new NotFoundException("EvaluationScheme", id!);
        if (id.HasValue) Token(row, input.ConcurrencyToken);
        if (row.Status != "DRAFT") throw new ConflictException("Published schemes are immutable; create a new version.");
        foreach (var c in row.Components) await ComponentAuthority(c.RubricId, row.ProjectId, ct);
        db.RemoveRange(row.Components); row.Components.Clear();
        row.Name = input.Name.Trim(); row.ProjectPeriodId = input.ProjectPeriodId; row.PassThreshold = input.PassThreshold;
        row.ConcurrencyToken = Guid.NewGuid();
        row.Components = input.Components.Select(c => new EvaluationSchemeComponent { Name = c.Name, Scope = c.Scope, MajorId = c.MajorId,
            RubricId = c.RubricId, ProjectWeightPercent = c.ProjectWeightPercent, StudentWeightPercent = c.StudentWeightPercent,
            RequiredEvaluators = c.RequiredEvaluators }).ToList();
        await Validate(row, ct);
        if (!id.HasValue) db.Add(row);
        await db.SaveChangesAsync(ct); await Audit("EVALUATION_SCHEME_SAVED", "EVALUATION_SCHEME", row.Id, Map(row), ct); return Map(row);
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
            await ComponentAuthority(component.RubricId, projectId, ct);
            Token(row, input.ConcurrencyToken);
            if (row.Status != "DRAFT") throw new ConflictException("Published scheme components are immutable.");
            var change = input.Component ?? throw new ValidationException(new Dictionary<string, string[]> { ["component"] = ["A component is required."] });
            if (change.Scope != component.Scope || change.MajorId != component.MajorId)
                throw new ConflictException("Component targets are changed through the lead department scheme workflow.");
            await ComponentAuthority(change.RubricId, projectId, ct);
            component.Name = change.Name;
            component.RubricId = change.RubricId;
            component.ProjectWeightPercent = change.ProjectWeightPercent;
            component.StudentWeightPercent = change.StudentWeightPercent;
            component.RequiredEvaluators = change.RequiredEvaluators;
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
        if (actor.IsAdmin || AcademicAuthority.IsLead(actor, project)) return rows.Select(Map).ToArray();
        var rubricIds = rows.SelectMany(r => r.Components.Select(c => c.RubricId)).Distinct().ToArray();
        var ownRubrics = (await db.Rubrics.Where(r => rubricIds.Contains(r.Id) && r.DepartmentId == actor.DepartmentId)
            .Select(r => r.Id).ToArrayAsync(ct)).ToHashSet();
        return rows.Select(r => Map(r)).Select(r => r with {
            Components = r.Components.Where(c => ownRubrics.Contains(c.RubricId)).ToArray(),
            Students = r.Students.Where(s => s.DepartmentId == actor.DepartmentId).ToArray() }).ToArray();
    }, ct);
    public Task<EvaluationSchemeDto> PublishAsync(long id, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var row = await LockedRow(id, ct); Token(row, token);
        if (row.Status != "DRAFT") throw new ConflictException("Scheme is already published.");
        await Validate(row, ct);
        var old = await db.Set<EvaluationScheme>().SingleOrDefaultAsync(x => x.ProjectId == row.ProjectId && x.Status == "PUBLISHED", ct);
        if (old is not null)
        {
            if (await db.Set<EvaluationAssignment>().AnyAsync(a => a.ProjectId == row.ProjectId && a.ComponentId != null, ct))
                throw new ConflictException("A project already using a scheme cannot replace its scoring history.");
            old.Status = "RETIRED"; await db.SaveChangesAsync(ct);
        }
        row.PolicyVersionId = await PolicyVersions.CaptureAsync(db, row.ProjectPeriodId, "EVALUATION_SCHEME", row.Id, Now, ct);
        row.Status = "PUBLISHED"; row.PublishedBy = Actor; row.PublishedAt = Now; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Audit("EVALUATION_SCHEME_PUBLISHED", "EVALUATION_SCHEME", id, Map(row), ct); return Map(row);
    }, ct);
    public Task<EvaluationSchemeDto> VersionAsync(long id, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var source = await LockedRow(id, ct); Token(source, token);
        if (source.Status == "DRAFT") throw new ConflictException("Edit the draft directly.");
        foreach (var c in source.Components) await ComponentAuthority(c.RubricId, source.ProjectId, ct);
        var row = new EvaluationScheme { RootId = source.RootId ?? source.Id, ProjectId = source.ProjectId, ProjectPeriodId = source.ProjectPeriodId,
            Version = await db.Set<EvaluationScheme>().Where(x => x.ProjectId == source.ProjectId).MaxAsync(x => x.Version, ct) + 1,
            Name = source.Name, PassThreshold = source.PassThreshold, CalculationRule = source.CalculationRule,
            CreatedBy = Actor, CreatedAt = Now, ConcurrencyToken = Guid.NewGuid(),
            StudentsJson = source.StudentsJson, RegistrationSnapshotJson = source.RegistrationSnapshotJson,
            Components = source.Components.Select(c => new EvaluationSchemeComponent { Name = c.Name, Scope = c.Scope, MajorId = c.MajorId,
                RubricId = c.RubricId, ProjectWeightPercent = c.ProjectWeightPercent, StudentWeightPercent = c.StudentWeightPercent, RequiredEvaluators = c.RequiredEvaluators }).ToList() };
        db.Add(row); await db.SaveChangesAsync(ct); await Audit("EVALUATION_SCHEME_VERSIONED", "EVALUATION_SCHEME", row.Id, Map(row), ct); return Map(row);
    }, ct);
    public async Task DeleteAsync(long id, string token, CancellationToken ct) => await evaluations.InTransactionAsync(async () =>
    {
        var row = await LockedRow(id, ct); Token(row, token);
        if (row.Status != "DRAFT") throw new ConflictException("Only drafts can be deleted.");
        foreach (var c in row.Components) await ComponentAuthority(c.RubricId, row.ProjectId, ct);
        db.Remove(row); await db.SaveChangesAsync(ct); await Audit("EVALUATION_SCHEME_DELETED", "EVALUATION_SCHEME", id, Map(row), ct); return true;
    }, ct);
    public async Task<ScopedAssignmentContext> ResolveAssignmentAsync(long projectId, AssignEvaluatorRequest input, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Assignment scope needs the project transaction.");
        if (input.ComponentId is null || input.Scope is null) throw new ConflictException("Specify a published scheme component and explicit scope; legacy assignments are read-only.");
        var component = await db.Set<EvaluationSchemeComponent>().SingleOrDefaultAsync(c => c.Id == input.ComponentId, ct)
            ?? throw new NotFoundException("SchemeComponent", input.ComponentId);
        var scheme = await Row(component.SchemeId, ct);
        if (scheme.ProjectId != projectId || scheme.ProjectPeriodId != input.ProjectPeriodId) throw new NotFoundException("SchemeComponent", input.ComponentId);
        if (scheme.Status != "PUBLISHED" || scheme.PolicyVersionId is null) throw new ConflictException("Publish the scheme before assigning evaluators.");
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
        var packageId = await db.Set<FinalSubmission>().Where(s => s.ProjectId == projectId).Select(s => s.Id).SingleAsync(ct);
        return new(component.Id, scheme.Id, component.Scope, component.MajorId, input.StudentId, component.RubricId,
            scheme.PolicyVersionId.Value, JsonSerializer.Serialize(new { Scheme = Map(scheme), component, input.StudentId,
                FinalSubmissionId = packageId, EvaluatorSource = input.EvaluationType }));
    }
    public async Task EnsureWritableAsync(EvaluationAssignmentRecord assignment, CancellationToken ct)
    {
        if (assignment.Scope == "UNKNOWN" || assignment.ComponentId is null || assignment.PolicyVersionId is null)
            throw new ConflictException("Legacy evaluations without proven scope are read-only. Create a scoped assignment.");
        var component = await db.Set<EvaluationSchemeComponent>().SingleAsync(c => c.Id == assignment.ComponentId, ct);
        var scheme = await Row(component.SchemeId, ct);
        if (scheme.Status != "PUBLISHED" || scheme.ProjectId != assignment.ProjectId || scheme.PolicyVersionId != assignment.PolicyVersionId
            || scheme.ProjectPeriodId != assignment.PeriodId
            || component.Scope != assignment.Scope || component.MajorId != assignment.MajorId || component.RubricId != assignment.RubricId)
            throw new ConflictException("Assignment does not match its frozen scheme.");
        if (assignment.Scope == "INDIVIDUAL" && (!assignment.StudentId.HasValue
            || !JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson)!.Any(s => s.StudentId == assignment.StudentId && s.MajorId == assignment.MajorId)))
            throw new ConflictException("Individual assignment is outside the frozen roster.");
    }
}
