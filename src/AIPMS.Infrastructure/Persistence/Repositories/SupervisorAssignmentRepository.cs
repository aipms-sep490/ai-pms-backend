using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class SupervisorAssignmentRepository(AipmsDbContext context,
    ISupervisorRequestRepository requests) : ISupervisorAssignmentRepository
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is not null) return await action(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            for (Exception? current = ex; current is not null; current = current.InnerException)
                if (current is DbUpdateConcurrencyException || current is SqlException { Number: 1205 or 1222 or 2601 or 2627 })
                    throw new ConflictException("The assignment, project or supervisor changed concurrently. Reload and retry.");
            throw;
        }
    }

    public async Task LockAsync(long assignmentId, CancellationToken ct)
    {
        RequireTransaction();
        await context.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.supervisor_assignments WITH (UPDLOCK, HOLDLOCK) WHERE id = {assignmentId}")
            .ToListAsync(ct);
        var keys = await context.SupervisorAssignments.AsNoTracking().Where(a => a.Id == assignmentId)
            .Select(a => new { a.SupervisorProfileId, a.ProjectId }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("SupervisorAssignment", assignmentId);
        // Use acceptance's capacity/project locks so releasing a slot and accepting new work serialize.
        await requests.LockSupervisorAndProjectAsync(keys.SupervisorProfileId, keys.ProjectId, ct);
    }

    public async Task<SupervisorAssignmentModel?> GetAsync(long assignmentId, CancellationToken ct)
    {
        var assignment = await context.SupervisorAssignments.AsNoTracking().Where(a => a.Id == assignmentId)
            .Select(SupervisorAssignmentMapper.Projection).SingleOrDefaultAsync(ct);
        if (assignment is null) return null;
        return (await WithScopeAsync([assignment], ct))[0];
    }

    private async Task<SupervisorAssignmentModel[]> WithScopeAsync(IReadOnlyList<SupervisorAssignmentModel> assignments, CancellationToken ct)
    {
        var projectIds = assignments.Select(a => a.ProjectId).Distinct().ToArray();
        var snapshots = context.Set<ProjectRegistrationSnapshot>().AsNoTracking();
        var rows = await snapshots.Where(s => projectIds.Contains(s.ProjectId)
            && !snapshots.Any(newer => newer.ProjectId == s.ProjectId && newer.Id > s.Id))
            .Select(s => new { s.ProjectId, s.SnapshotJson }).ToListAsync(ct);
        var known = rows.Where(s => ProjectAcademicScopeReader.ParseEvidence(s.SnapshotJson) is not null)
            .Select(s => s.ProjectId).ToHashSet();
        return assignments.Select(a => a with { HasKnownAcademicScope = known.Contains(a.ProjectId) }).ToArray();
    }

    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) =>
        context.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct);

    public async Task<bool> IsProjectDepartmentAsync(long projectId, long departmentId, CancellationToken ct) =>
        (await AIPMS.Infrastructure.Services.Projects.ProjectAcademicScopeReader.ReadAsync(context, projectId, ct))
            .DepartmentIds.Contains(departmentId);

    public async Task<bool> CanReplaceAsync(long assignmentId, long departmentId, CancellationToken ct)
    {
        return await context.SupervisorAssignments.AnyAsync(a => a.Id == assignmentId && a.EndedAt == null
            && a.Project.Status == "ACTIVE", ct) && await IsAssignmentDepartmentAsync(assignmentId, departmentId, ct);
    }

    public async Task<bool> IsAssignmentDepartmentAsync(long assignmentId, long departmentId, CancellationToken ct)
    {
        var assignment = await context.SupervisorAssignments.AsNoTracking()
            .Where(a => a.Id == assignmentId)
            .Select(a => new { a.ProjectId, a.IsPrimary, a.AssignmentType, a.MajorId, a.EndedAt, a.Project.Status })
            .SingleOrDefaultAsync(ct);
        if (assignment is null) return false;
        var scope = await AIPMS.Infrastructure.Services.Projects.ProjectAcademicScopeReader.ReadAsync(context, assignment.ProjectId, ct, requireFrozenScope: true);
        long? authority = assignment.IsPrimary && assignment.AssignmentType == "PRIMARY" ? scope.LeadDepartmentId : null;
        if (!assignment.IsPrimary && assignment.AssignmentType == "DISCIPLINE_MENTOR"
            && assignment.MajorId is long major && scope.MajorIds.Contains(major))
            authority = scope.MajorDepartmentIds is not null && scope.MajorDepartmentIds.TryGetValue(major, out var frozen)
                ? frozen : null;
        return authority == departmentId && scope.DepartmentIds.Contains(departmentId);
    }

    public async Task<IReadOnlySet<long>> GetReplaceableIdsAsync(long projectId, long departmentId, CancellationToken ct)
    {
        var scope = await AIPMS.Infrastructure.Services.Projects.ProjectAcademicScopeReader.ReadAsync(context, projectId, ct, requireFrozenScope: true);
        if (!scope.DepartmentIds.Contains(departmentId)) return new HashSet<long>();
        var majors = scope.MajorDepartmentIds?.Where(m => m.Value == departmentId && scope.MajorIds.Contains(m.Key))
            .Select(m => m.Key).ToArray() ?? [];
        return (await context.SupervisorAssignments.AsNoTracking().Where(a => a.ProjectId == projectId
            && a.EndedAt == null && a.Project.Status == "ACTIVE"
            && ((a.IsPrimary && a.AssignmentType == "PRIMARY" && scope.LeadDepartmentId == departmentId)
                || (!a.IsPrimary && a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId.HasValue && majors.Contains(a.MajorId.Value))))
            .Select(a => a.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task<PagedResult<SupervisorAssignmentModel>> SearchAsync(SupervisorAssignmentSearch search, CancellationToken ct)
    {
        if (search.ProjectId is null && search.SupervisorUserId is null)
            throw new InvalidOperationException("Assignment queries must have a project or supervisor scope.");
        var query = context.SupervisorAssignments.AsNoTracking();
        if (search.ProjectId.HasValue) query = query.Where(a => a.ProjectId == search.ProjectId);
        if (search.SupervisorUserId.HasValue) query = query.Where(a => a.SupervisorProfile.UserId == search.SupervisorUserId);
        if (search.Status == "ACTIVE") query = query.Where(a => a.EndedAt == null);
        if (search.Status == "ENDED") query = query.Where(a => a.EndedAt != null);
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(a => a.AssignedAt).ThenByDescending(a => a.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(SupervisorAssignmentMapper.Projection).ToListAsync(ct);
        return new(await WithScopeAsync(items, ct), search.Page, search.PageSize, count);
    }

    public async Task<SupervisorAssignmentModel> EndAsync(long assignmentId, DateTime now, CancellationToken ct, long? actorId = null, string? reason = null)
    {
        RequireTransaction();
        var assignment = await context.SupervisorAssignments.SingleAsync(a => a.Id == assignmentId, ct);
        if (assignment.EndedAt.HasValue) throw new ConflictException("The assignment has already ended.");
        assignment.EndedAt = now;
        assignment.EndedBy = actorId;
        assignment.EndReason = reason;
        assignment.UpdatedAt = now;
        await context.SaveChangesAsync(ct);
        return (await GetAsync(assignmentId, ct))!;
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Assignment mutations require a transaction including permission checks and audit.");
    }
}
