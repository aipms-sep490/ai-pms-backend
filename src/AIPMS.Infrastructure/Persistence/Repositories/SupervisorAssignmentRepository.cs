using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
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

    public Task<SupervisorAssignmentModel?> GetAsync(long assignmentId, CancellationToken ct) =>
        context.SupervisorAssignments.AsNoTracking().Where(a => a.Id == assignmentId)
            .Select(SupervisorAssignmentMapper.Projection).SingleOrDefaultAsync(ct);

    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) =>
        context.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct);

    public Task<bool> IsProjectDepartmentAsync(long projectId, long departmentId, CancellationToken ct) =>
        context.ProjectMajors.AsNoTracking().AnyAsync(m => m.ProjectId == projectId
            && m.Major.DepartmentId == departmentId && m.Major.IsActive
            && m.Major.Department.IsActive && m.Major.Department.Organization.IsActive, ct);

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
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<SupervisorAssignmentModel> EndAsync(long assignmentId, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var assignment = await context.SupervisorAssignments.SingleAsync(a => a.Id == assignmentId, ct);
        if (assignment.EndedAt.HasValue) throw new ConflictException("The assignment has already ended.");
        assignment.EndedAt = now;
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
