using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class EvaluationDraftRepository(AipmsDbContext db) : IEvaluationDraftRepository
{
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await action();
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (Conflict(ex))
        {
            throw new ConflictException("The evaluation, assignment or academic configuration changed. Reload and retry.");
        }
    }

    private static bool Conflict(Exception ex)
    {
        for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
            if (cause is DbUpdateConcurrencyException || cause is SqlException { Number: 1205 or 1222 or 2601 or 2627 or 547 }) return true;
        return false;
    }

    public async Task LockProjectAsync(long id, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Evaluation writes require a transaction.");
        var found = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.projects WITH (XLOCK, HOLDLOCK) WHERE id = {id}").ToListAsync(ct);
        if (found.Count == 0) throw new NotFoundException("Project", id);
    }

    public async Task<EvaluationActor?> GetActorAsync(long id, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == id && u.Status == "ACTIVE")
            .Select(u => new EvaluationActor(u.Id, u.DepartmentId,
                u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
                    && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
                u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
                    && u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER"))).SingleOrDefaultAsync(ct);

    public async Task<EvaluationProject?> GetProjectAsync(long id, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Status, Semester = p.Team.AcademicSemesterId,
                Organization = p.Team.AcademicSemester.OrganizationId,
                Active = p.Team.AcademicSemester.Organization.IsActive && p.ProjectMajors.Any()
                    && p.ProjectMajors.All(m => m.Major.IsActive && m.Major.Department.IsActive
                        && m.Major.Department.Organization.IsActive
                        && m.Major.Department.OrganizationId == p.Team.AcademicSemester.OrganizationId),
                Departments = p.ProjectMajors.Select(m => m.Major.DepartmentId).Distinct().ToList() }).SingleOrDefaultAsync(ct);
        return project is null ? null : new(project.Id, project.Semester, project.Organization, project.Status, project.Active, project.Departments);
    }

    public async Task<EvaluationPeriod?> GetPeriodAsync(long id, DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var period = await db.ProjectPeriods.AsNoTracking().Where(p => p.Id == id).Select(p => new
        {
            p.Id, p.AcademicSemesterId, p.RubricId,
            Open = p.PeriodType == "EVALUATION" && p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt
                && p.AcademicSemester.Status == "ACTIVE" && p.AcademicSemester.StartDate <= today
                && today <= p.AcademicSemester.EndDate && p.AcademicSemester.Organization.IsActive
        }).SingleOrDefaultAsync(ct);
        if (period is null) return null;
        var count = await db.ProjectPeriods.CountAsync(p => p.AcademicSemesterId == period.AcademicSemesterId
            && p.PeriodType == "EVALUATION" && p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt, ct);
        return new(period.Id, period.AcademicSemesterId, period.RubricId, period.Open && count == 1);
    }

    public Task<bool> IsCurrentSupervisorAsync(long projectId, long userId, CancellationToken ct) =>
        db.SupervisorAssignments.AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null && a.IsPrimary
            && a.SupervisorProfile.UserId == userId, ct);

    private static EvaluationAssignmentRecord Map(EvaluationAssignment a) => new(a.Id, a.ProjectId, a.EvaluatorId,
        a.RubricId, a.ProjectPeriodId, a.DepartmentId, a.EvaluationType, a.Status, a.AssignedBy,
        a.AssignedAt, a.RevokedAt, a.ConcurrencyToken.ToString("N"));

    public async Task<EvaluationAssignmentRecord?> GetAssignmentAsync(long id, CancellationToken ct)
    {
        var row = await db.Set<EvaluationAssignment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        return row is null ? null : Map(row);
    }

    public async Task<EvaluationAssignmentRecord> AssignAsync(long projectId, long evaluatorId, long periodId,
        long rubricId, long departmentId, string type, long actorId, DateTime now, CancellationToken ct)
    {
        if (await db.Set<EvaluationAssignment>().AnyAsync(a => a.ProjectId == projectId && a.EvaluatorId == evaluatorId
            && a.EvaluationType == type && a.Status == "ACTIVE", ct))
            throw new ConflictException("This evaluator already has an active assignment of this type for the project.");
        var row = new EvaluationAssignment { ProjectId = projectId, EvaluatorId = evaluatorId,
            ProjectPeriodId = periodId, RubricId = rubricId, DepartmentId = departmentId,
            EvaluationType = type, AssignedBy = actorId, AssignedAt = now, ConcurrencyToken = Guid.NewGuid() };
        db.Set<EvaluationAssignment>().Add(row);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<EvaluationAssignmentRecord> RevokeAsync(long id, string reason, DateTime now, CancellationToken ct)
    {
        var row = await db.Set<EvaluationAssignment>().SingleAsync(a => a.Id == id, ct);
        row.Status = "REVOKED";
        row.RevokedAt = now;
        row.RevocationReason = reason;
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    private IQueryable<EvaluationAssignment> EligibleAssignments(EvaluationActor actor, bool manager) =>
        db.Set<EvaluationAssignment>().AsNoTracking().Where(a =>
            (manager && (actor.IsAdmin || (actor.IsStaff && a.DepartmentId == actor.DepartmentId)))
            || (actor.IsLecturer && a.EvaluatorId == actor.Id && a.DepartmentId == actor.DepartmentId && a.Status == "ACTIVE"
                && db.Projects.Any(p => p.Id == a.ProjectId && p.Team.AcademicSemester.Organization.IsActive
                    && p.ProjectMajors.Any(m => m.Major.DepartmentId == a.DepartmentId && m.Major.IsActive
                        && m.Major.Department.IsActive && m.Major.Department.OrganizationId == p.Team.AcademicSemester.OrganizationId)
                    && p.ProjectMajors.All(m => m.Major.IsActive && m.Major.Department.IsActive && m.Major.Department.Organization.IsActive
                        && m.Major.Department.OrganizationId == p.Team.AcademicSemester.OrganizationId))
                && (a.EvaluationType != "SUPERVISOR" || db.SupervisorAssignments.Any(s => s.ProjectId == a.ProjectId
                    && s.EndedAt == null && s.IsPrimary && s.SupervisorProfile.UserId == actor.Id))));

    public async Task<PagedResult<EvaluationAssignmentRecord>> ListAssignmentsAsync(long? projectId, EvaluationActor actor,
        string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = EligibleAssignments(actor, projectId.HasValue);
        if (projectId.HasValue) query = query.Where(a => a.ProjectId == projectId);
        if (status is not null) query = query.Where(a => a.Status == status);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(Map).ToArray(), page, pageSize, total);
    }

    public async Task<EvaluationDraftRecord?> GetDraftAsync(long id, CancellationToken ct)
    {
        var state = await db.Set<EvaluationDraftState>().AsNoTracking().SingleOrDefaultAsync(s => s.EvaluationId == id, ct);
        // Legacy grades have no verified assignment/calculation policy; never infer editing authority.
        if (state is null) return null;
        var row = await db.Evaluations.AsNoTracking().Include(e => e.Rubric).Include(e => e.EvaluationDetails)
            .SingleOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return null;
        var assignment = await db.Set<EvaluationAssignment>().AsNoTracking().SingleAsync(a => a.Id == state.AssignmentId, ct);
        var rubricVersion = await db.Set<RubricVersion>().AsNoTracking().SingleOrDefaultAsync(v => v.RubricId == row.RubricId, ct)
            ?? throw new ConflictException("The evaluation rubric has no protected version metadata.");
        if (row.ProjectId != assignment.ProjectId || row.EvaluatorId != assignment.EvaluatorId || row.RubricId != assignment.RubricId
            || row.EvaluationType != assignment.EvaluationType || state.CalculationRule != "WEIGHTED_10_AWAY_FROM_ZERO_2DP_V1")
            throw new ConflictException("Evaluation metadata does not match its protected assignment.");
        var criteria = await db.RubricCriteria.AsNoTracking().Include(c => c.Criterion).Where(c => c.RubricId == row.RubricId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
        if (row.EvaluationDetails.Any(d => criteria.All(c => c.Id != d.RubricCriterionId)))
            throw new ConflictException("Evaluation contains a score outside its protected rubric.");
        var details = row.EvaluationDetails.ToDictionary(d => d.RubricCriterionId);
        return new(row.Id, state.AssignmentId, row.ProjectId, row.EvaluatorId, row.RubricId, row.Rubric.Name,
            rubricVersion.RootRubricId, rubricVersion.VersionNumber, row.EvaluationType,
            row.Status, row.Comments, row.TotalScore, state.ConcurrencyToken.ToString("N"), row.CreatedAt, row.UpdatedAt,
            criteria.Select(c => new EvaluationScoreRecord(c.Id, c.Criterion.Name, c.Criterion.Description, c.WeightPercent, c.MaxScore,
                c.SortOrder, c.IsRequired, details.GetValueOrDefault(c.Id)?.Score, details.GetValueOrDefault(c.Id)?.Comments)).ToArray());
    }

    public async Task<EvaluationDraftRecord?> FindDraftAsync(long assignmentId, CancellationToken ct)
    {
        var id = await db.Set<EvaluationDraftState>().Where(s => s.AssignmentId == assignmentId).Select(s => (long?)s.EvaluationId).SingleOrDefaultAsync(ct);
        return id.HasValue ? await GetDraftAsync(id.Value, ct) : null;
    }

    public async Task<EvaluationDraftRecord> CreateDraftAsync(EvaluationAssignmentRecord assignment, DateTime now, CancellationToken ct)
    {
        var row = new M.Evaluation { ProjectId = assignment.ProjectId, EvaluatorId = assignment.EvaluatorId,
            RubricId = assignment.RubricId, EvaluationType = assignment.EvaluationType, Status = "DRAFT", CreatedAt = now, UpdatedAt = now };
        db.Evaluations.Add(row);
        await db.SaveChangesAsync(ct);
        db.Set<EvaluationDraftState>().Add(new() { EvaluationId = row.Id, AssignmentId = assignment.Id, ConcurrencyToken = Guid.NewGuid() });
        await db.SaveChangesAsync(ct);
        return (await GetDraftAsync(row.Id, ct))!;
    }

    public async Task<EvaluationDraftRecord> SaveAsync(long id, IReadOnlyList<EvaluationScoreInput> scores,
        string? comments, decimal? total, DateTime now, CancellationToken ct)
    {
        var row = await db.Evaluations.Include(e => e.EvaluationDetails).SingleAsync(e => e.Id == id, ct);
        var state = await db.Set<EvaluationDraftState>().SingleAsync(s => s.EvaluationId == id, ct);
        // Preserve detail IDs for retained scores; omission explicitly clears a draft score.
        var inputs = scores.ToDictionary(s => s.RubricCriterionId);
        foreach (var detail in row.EvaluationDetails.ToArray())
        {
            if (!inputs.ContainsKey(detail.RubricCriterionId)) db.EvaluationDetails.Remove(detail);
        }
        foreach (var input in scores)
        {
            var detail = row.EvaluationDetails.SingleOrDefault(d => d.RubricCriterionId == input.RubricCriterionId);
            if (detail is null)
            {
                detail = new M.EvaluationDetail { RubricCriterionId = input.RubricCriterionId, CreatedAt = now };
                row.EvaluationDetails.Add(detail);
            }
            detail.Score = input.Score!.Value;
            detail.Comments = input.Comments?.Trim();
            detail.UpdatedAt = now;
        }
        row.Comments = comments;
        row.TotalScore = total;
        row.UpdatedAt = now;
        state.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return (await GetDraftAsync(id, ct))!;
    }

    public async Task<PagedResult<EvaluationDraftRecord>> ListDraftsAsync(long projectId, EvaluationActor actor, int page, int pageSize, CancellationToken ct)
    {
        var assignments = EligibleAssignments(actor, true).Where(a => a.ProjectId == projectId);
        var query = db.Set<EvaluationDraftState>().Where(s => assignments.Any(a => a.Id == s.AssignmentId));
        var total = await query.LongCountAsync(ct);
        var ids = await query.OrderByDescending(s => s.EvaluationId).Skip((page - 1) * pageSize).Take(pageSize).Select(s => s.EvaluationId).ToListAsync(ct);
        var results = new List<EvaluationDraftRecord>();
        foreach (var id in ids) results.Add((await GetDraftAsync(id, ct))!);
        return new(results, page, pageSize, total);
    }
}
