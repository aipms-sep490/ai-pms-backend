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

internal sealed class RubricRepository(AipmsDbContext context) : IRubricRepository
{
    public async Task<RubricActor?> GetActorAsync(long userId, CancellationToken ct)
    {
        var user = await context.Users.AsNoTracking().Where(u => u.Id == userId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.Id, u.DepartmentId,
                Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
                ActiveScope = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
            }).SingleOrDefaultAsync(ct);
        if (user is null || (!user.Admin && (!user.Staff || !user.ActiveScope))) return null;
        return new(user.Id, user.Admin, user.DepartmentId);
    }

    public async Task<RubricScope?> GetScopeAsync(long departmentId, long semesterId, CancellationToken ct) =>
        await (from department in context.Departments.AsNoTracking()
               from semester in context.AcademicSemesters.AsNoTracking()
               where department.Id == departmentId && semester.Id == semesterId
                   && department.IsActive && department.Organization.IsActive
                   && department.OrganizationId == semester.OrganizationId
               select new RubricScope(department.Id, department.OrganizationId, semester.Status)).SingleOrDefaultAsync(ct);

    private IQueryable<M.Rubric> Visible(RubricActor actor) => context.Rubrics.AsNoTracking()
        .Where(r => actor.IsAdmin || (r.DepartmentId == actor.DepartmentId
            && r.Department != null && r.Department.IsActive && r.Department.Organization.IsActive
            && r.AcademicSemester != null && r.AcademicSemester.OrganizationId == r.Department.OrganizationId));

    public Task<PagedResult<RubricRecord>> ListAsync(RubricActor actor, RubricFilter filter, CancellationToken ct) => Transaction(async () =>
    {
        // Keep content and its edit token from the same committed version across the reads.
        var query = Visible(actor);
        if (filter.DepartmentId.HasValue) query = query.Where(r => r.DepartmentId == filter.DepartmentId);
        if (filter.AcademicSemesterId.HasValue) query = query.Where(r => r.AcademicSemesterId == filter.AcademicSemesterId);
        if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(r => r.Code.Contains(filter.Search) || r.Name.Contains(filter.Search));
        if (filter.Status is not null)
            query = query.Where(r => context.Set<RubricVersion>().Any(v => v.RubricId == r.Id && v.Status == filter.Status));
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(r => r.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Include(r => r.RubricCriteria).ThenInclude(c => c.Criterion).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToArray();
        var versions = await context.Set<RubricVersion>().AsNoTracking().Where(v => ids.Contains(v.RubricId)).ToDictionaryAsync(v => v.RubricId, ct);
        var referenced = await context.Rubrics.Where(r => ids.Contains(r.Id) &&
                (r.Evaluations.Any() || r.ProjectPeriods.Any() || r.RubricCriteria.Any(c => c.EvaluationDetails.Any())))
            .Select(r => r.Id).ToListAsync(ct);
        return new PagedResult<RubricRecord>(rows.Select(r => Map(r, versions.GetValueOrDefault(r.Id), referenced.Contains(r.Id))).ToArray(), filter.Page, filter.PageSize, count);
    }, System.Data.IsolationLevel.RepeatableRead, ct);

    public Task<RubricRecord?> GetAsync(long id, RubricActor actor, bool forUpdate, CancellationToken ct) => forUpdate
        ? GetCore(id, actor, true, ct)
        : Transaction(() => GetCore(id, actor, false, ct), System.Data.IsolationLevel.RepeatableRead, ct);

    private async Task<RubricRecord?> GetCore(long id, RubricActor actor, bool forUpdate, CancellationToken ct)
    {
        if (!await Visible(actor).AnyAsync(r => r.Id == id, ct)) return null;
        if (forUpdate)
        {
            // All family mutations take the root lock first, avoiding sibling-version lock inversion.
            var root = await context.Set<RubricVersion>().Where(v => v.RubricId == id).Select(v => (long?)v.RootRubricId).SingleOrDefaultAsync(ct);
            if (root is null) throw new ConflictException("Rubric version metadata is missing. Apply the rubric migration before managing legacy rows.");
            await Lock(root.Value, ct);
            if (root != id) await Lock(id, ct);
        }
        return await Read(id, ct);
    }

    private async Task Lock(long id, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Rubric mutation requires a transaction.");
        await context.Rubrics.FromSqlInterpolated($"SELECT * FROM dbo.rubrics WITH (XLOCK, HOLDLOCK) WHERE id = {id}")
            .AsNoTracking().SingleAsync(ct);
    }

    private async Task<RubricRecord> Read(long id, CancellationToken ct)
    {
        var row = await context.Rubrics.AsNoTracking().Include(r => r.RubricCriteria).ThenInclude(c => c.Criterion).SingleAsync(r => r.Id == id, ct);
        var version = await context.Set<RubricVersion>().AsNoTracking().SingleOrDefaultAsync(v => v.RubricId == id, ct);
        var referenced = await context.Rubrics.Where(r => r.Id == id).AnyAsync(r => r.Evaluations.Any()
            || r.ProjectPeriods.Any() || r.RubricCriteria.Any(c => c.EvaluationDetails.Any()), ct);
        return Map(row, version, referenced);
    }

    private static RubricRecord Map(M.Rubric r, RubricVersion? v, bool referenced) => new(r.Id, r.DepartmentId,
        r.AcademicSemesterId, r.Code, r.Name, r.Description,
        v?.Status ?? (r.IsActive ? RubricStatuses.Published : RubricStatuses.Retired),
        v?.RootRubricId ?? r.Id, v?.VersionNumber ?? 1, v?.ConcurrencyToken.ToString("N") ?? "", referenced,
        r.CreatedAt, r.UpdatedAt, r.RubricCriteria.OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new RubricCriterionRecord(c.Id, c.CriterionId, c.Criterion.Name, c.Criterion.Description,
                c.WeightPercent, c.MaxScore, c.SortOrder, c.IsRequired)).ToArray());

    public async Task<RubricRecord> CreateAsync(long departmentId, long semesterId, string code, string name,
        string? description, IReadOnlyList<RubricCriterionInput> criteria, long actorId, DateTime now,
        long? sourceId, CancellationToken ct)
    {
        var source = sourceId.HasValue
            ? await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == sourceId.Value, ct) : null;
        var versionNumber = source is null ? 1 : 1 + await context.Set<RubricVersion>()
            .Where(v => v.RootRubricId == source.RootRubricId).MaxAsync(v => v.VersionNumber, ct);
        var row = new M.Rubric { DepartmentId = departmentId, AcademicSemesterId = semesterId,
            Code = code, Name = name, Description = description, IsActive = false, CreatedBy = actorId,
            CreatedAt = now, UpdatedAt = now, RubricCriteria = Criteria(criteria, now) };
        context.Rubrics.Add(row);
        await context.SaveChangesAsync(ct);
        context.Set<RubricVersion>().Add(new RubricVersion { RubricId = row.Id, RootRubricId = source?.RootRubricId ?? row.Id,
            VersionNumber = versionNumber, Status = RubricStatuses.Draft, ConcurrencyToken = Guid.NewGuid() });
        await context.SaveChangesAsync(ct);
        return await Read(row.Id, ct);
    }

    private static List<M.RubricCriterion> Criteria(IReadOnlyList<RubricCriterionInput> input, DateTime now) => input.Select(c =>
        new M.RubricCriterion
        {
            WeightPercent = c.WeightPercent, MaxScore = c.MaxScore, SortOrder = c.SortOrder,
            IsRequired = c.IsRequired, CreatedAt = now, UpdatedAt = now,
            // Own a distinct definition for every version; never mutate a shared criterion catalogue row.
            Criterion = new M.EvaluationCriterion { Code = "RC_" + Guid.NewGuid().ToString("N"),
                Name = c.Name.Trim(), Description = c.Description?.Trim(), IsActive = true, CreatedAt = now, UpdatedAt = now }
        }).ToList();

    private async Task RemoveCriteria(long id, CancellationToken ct)
    {
        var old = await context.RubricCriteria.Where(c => c.RubricId == id).ToListAsync(ct);
        var ids = old.Select(c => c.CriterionId).ToArray();
        context.RubricCriteria.RemoveRange(old);
        await context.SaveChangesAsync(ct);
        var unused = await context.EvaluationCriteria.Where(c => ids.Contains(c.Id) && !c.RubricCriteria.Any()).ToListAsync(ct);
        context.EvaluationCriteria.RemoveRange(unused);
        await context.SaveChangesAsync(ct);
    }

    public async Task<RubricRecord> UpdateAsync(long id, string name, string? description,
        IReadOnlyList<RubricCriterionInput> criteria, DateTime now, CancellationToken ct)
    {
        await RemoveCriteria(id, ct);
        var row = await context.Rubrics.SingleAsync(r => r.Id == id, ct);
        row.Name = name;
        row.Description = description;
        row.UpdatedAt = now;
        row.RubricCriteria = Criteria(criteria, now);
        var version = await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == id, ct);
        version.ConcurrencyToken = Guid.NewGuid();
        await context.SaveChangesAsync(ct);
        return await Read(id, ct);
    }

    public async Task<RubricRecord> SetStatusAsync(long id, string status, DateTime now, CancellationToken ct)
    {
        var row = await context.Rubrics.SingleAsync(r => r.Id == id, ct);
        var version = await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == id, ct);
        row.IsActive = status == RubricStatuses.Published;
        row.UpdatedAt = now;
        version.Status = status;
        version.ConcurrencyToken = Guid.NewGuid();
        await context.SaveChangesAsync(ct);
        return await Read(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        if (await context.Set<RubricVersion>().AnyAsync(v => v.RootRubricId == id && v.RubricId != id, ct))
            throw new ConflictException("A rubric that anchors other versions cannot be deleted.");
        await RemoveCriteria(id, ct);
        context.Set<RubricVersion>().Remove(await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == id, ct));
        await context.SaveChangesAsync(ct);
        context.Rubrics.Remove(await context.Rubrics.SingleAsync(r => r.Id == id, ct));
        await context.SaveChangesAsync(ct);
    }

    public Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct) =>
        Transaction(action, System.Data.IsolationLevel.ReadCommitted, ct);

    private async Task<T> Transaction<T>(Func<Task<T>> action, System.Data.IsolationLevel isolation, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(isolation, ct);
        try
        {
            var result = await action();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The rubric has changed. Reload it before retrying.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("The rubric code or version already exists.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            throw new ConflictException("The rubric is referenced or its academic scope has changed.");
        }
        catch (Exception ex) when (IsLockConflict(ex))
        {
            throw new ConflictException("The rubric is being changed by another request. Reload it before retrying.");
        }
    }

    private static bool IsLockConflict(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
            if (cause is SqlException { Number: 1205 or 1222 }) return true;
        return false;
    }
}
