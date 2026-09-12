using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Topics.Abstractions;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class TopicRepository(AipmsDbContext db) : ITopicRepository
{
    public async Task<TopicActor?> GetActorAsync(long userId, IReadOnlyCollection<string> tokenRoles, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.Status == "ACTIVE").Select(u => new
        {
            u.Id, u.DepartmentId, u.MajorId, OrganizationId = u.Department == null ? (long?)null : u.Department.OrganizationId,
            ActiveScope = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
            Eligible = u.Major != null && u.Major.IsActive && u.Major.DepartmentId == u.DepartmentId,
            Roles = u.UserRoleUsers.Select(r => r.Role.Code).ToArray()
        }).SingleOrDefaultAsync(ct);
        if (user is null) return null;
        var roles = user.Roles.Intersect(tokenRoles, StringComparer.Ordinal).ToHashSet();
        return new(user.Id, user.DepartmentId, user.OrganizationId, user.MajorId, roles.Contains(AppRoles.Admin),
            roles.Contains(AppRoles.DepartmentStaff), roles.Contains(AppRoles.Lecturer), roles.Contains(AppRoles.Student),
            user.ActiveScope, user.ActiveScope && user.Eligible);
    }

    public async Task<TopicPeriod?> GetPeriodAsync(long periodId, CancellationToken ct) =>
        await db.ProjectPeriods.AsNoTracking().Where(p => p.Id == periodId).Select(p => new TopicPeriod(p.Id,
            p.AcademicSemesterId, p.AcademicSemester.OrganizationId, p.PeriodType, p.Status, p.AcademicSemester.Status,
            p.StartAt, p.EndAt, p.AcademicSemester.EndDate, p.AcademicSemester.Organization.IsActive)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TopicMajor>> GetMajorsAsync(IReadOnlyList<long> ids, CancellationToken ct) =>
        await db.Majors.AsNoTracking().Where(m => ids.Contains(m.Id)).Select(m => new TopicMajor(m.Id,
            m.DepartmentId, m.Department.OrganizationId, m.IsActive && m.Department.IsActive && m.Department.Organization.IsActive)).ToArrayAsync(ct);

    public Task<bool> IsActiveDepartmentAsync(long departmentId, long organizationId, CancellationToken ct) =>
        db.Departments.AnyAsync(d => d.Id == departmentId && d.OrganizationId == organizationId && d.IsActive && d.Organization.IsActive, ct);

    private static IQueryable<ProjectTopic> Visible(IQueryable<ProjectTopic> query, TopicActor actor) => query.Where(t =>
        actor.IsAdmin || (actor.HasActiveScope && t.Period.AcademicSemester.OrganizationId == actor.OrganizationId
            && (t.Status == "PUBLISHED" && (actor.IsStudent || actor.IsLecturer || actor.IsStaff)
                || actor.IsStaff && t.LeadDepartmentId == actor.DepartmentId
                || actor.IsLecturer && t.CreatedBy == actor.Id && t.LeadDepartmentId == actor.DepartmentId)));

    private static IQueryable<ProjectTopic> Include(IQueryable<ProjectTopic> query) => query
        .Include(t => t.Period).ThenInclude(p => p.AcademicSemester)
        .Include(t => t.LeadDepartment)
        .Include(t => t.Requirements).ThenInclude(r => r.Major)
        .Include(t => t.Requirements).ThenInclude(r => r.Department);

    public async Task<TopicDto?> GetAsync(long id, TopicActor actor, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate && db.Database.CurrentTransaction is null) throw new InvalidOperationException("Topic mutations require a transaction.");
        var query = forUpdate ? db.Set<ProjectTopic>()
            .FromSqlInterpolated($"SELECT * FROM dbo.project_topics WITH (UPDLOCK, ROWLOCK) WHERE id = {id}").AsTracking()
            : db.Set<ProjectTopic>().AsNoTracking().Where(t => t.Id == id);
        var topic = await Include(Visible(query, actor)).SingleOrDefaultAsync(ct);
        return topic?.ToDto(actor);
    }

    public async Task<PagedResult<TopicDto>> ListAsync(TopicActor actor, TopicFilter filter, CancellationToken ct)
    {
        var query = Visible(db.Set<ProjectTopic>().AsNoTracking(), actor).Where(t => t.Status == filter.Status);
        if (filter.AcademicSemesterId.HasValue) query = query.Where(t => t.Period.AcademicSemesterId == filter.AcademicSemesterId);
        if (filter.ProjectPeriodId.HasValue) query = query.Where(t => t.ProjectPeriodId == filter.ProjectPeriodId);
        if (filter.DepartmentId.HasValue) query = query.Where(t => t.Requirements.Any(r => r.DepartmentId == filter.DepartmentId));
        if (filter.MajorId.HasValue) query = query.Where(t => t.Requirements.Any(r => r.MajorId == filter.MajorId));
        if (filter.ProjectMode is not null) query = query.Where(t => t.ProjectMode == filter.ProjectMode);
        if (filter.MineOnly) query = query.Where(t => t.CreatedBy == actor.Id);
        if (filter.CompatibleOnly) query = query.Where(t => actor.IsStudent && actor.HasEligibleStudentProfile
            && t.Requirements.Any(r => r.MajorId == actor.MajorId));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(t => t.Code.Contains(search) || t.Title.Contains(search) || (t.Domain != null && t.Domain.Contains(search)));
        }
        var count = await query.LongCountAsync(ct);
        var rows = await Include(query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)).ToArrayAsync(ct);
        return new(rows.Select(r => r.ToDto(actor)).ToArray(), filter.Page, filter.PageSize, count);
    }

    private static void Content(ProjectTopic row, TopicContentRequest input)
    {
        row.Title = input.Title.Trim(); row.Description = input.Description?.Trim();
        row.ProblemStatement = input.ProblemStatement?.Trim(); row.Objectives = input.Objectives?.Trim();
        row.ExpectedOutput = input.ExpectedOutput?.Trim(); row.Domain = input.Domain?.Trim();
        row.TechnologiesJson = JsonSerializer.Serialize(input.Technologies.Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
        row.KeywordsJson = JsonSerializer.Serialize(input.Keywords.Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
        row.ProjectMode = input.ProjectMode; row.PrimaryMajorId = input.PrimaryMajorId;
    }

    private async Task<List<TopicMajorRequirement>> Requirements(TopicContentRequest input, CancellationToken ct)
    {
        var majors = await GetMajorsAsync(input.Requirements.Select(r => r.MajorId).ToArray(), ct);
        return input.Requirements.Select(r => new TopicMajorRequirement { MajorId = r.MajorId,
            DepartmentId = majors.Single(m => m.Id == r.MajorId).DepartmentId, MinMembers = r.MinMembers,
            MaxMembers = r.MaxMembers, Responsibility = r.Responsibility.Trim() }).ToList();
    }

    public async Task<TopicDto> CreateAsync(CreateTopicRequest input, TopicActor actor, DateTime now, CancellationToken ct)
    {
        var topic = new ProjectTopic { ProjectPeriodId = input.ProjectPeriodId, LeadDepartmentId = input.LeadDepartmentId,
            Code = input.Code.Trim().ToUpperInvariant(), CreatedBy = actor.Id, UpdatedBy = actor.Id,
            CreatedAt = now, UpdatedAt = now, ConcurrencyToken = Guid.NewGuid() };
        Content(topic, input.Content);
        topic.Requirements = await Requirements(input.Content, ct);
        db.Add(topic); await db.SaveChangesAsync(ct);
        return (await GetAsync(topic.Id, actor, false, ct))!;
    }

    public async Task<TopicDto> UpdateAsync(long id, TopicContentRequest content, TopicActor actor, DateTime now, CancellationToken ct)
    {
        var topic = await db.Set<ProjectTopic>().Include(t => t.Requirements).SingleAsync(t => t.Id == id, ct);
        db.RemoveRange(topic.Requirements);
        await db.SaveChangesAsync(ct);
        topic.Requirements = await Requirements(content, ct);
        Content(topic, content);
        topic.ConcurrencyToken = Guid.NewGuid(); topic.UpdatedBy = actor.Id; topic.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, actor, false, ct))!;
    }

    public async Task<TopicDto> SetStatusAsync(long id, bool publish, string? reason, TopicActor actor, DateTime now, CancellationToken ct)
    {
        var topic = await db.Set<ProjectTopic>().Include(t => t.Requirements).SingleAsync(t => t.Id == id, ct);
        topic.Status = publish ? "PUBLISHED" : "CLOSED";
        if (publish)
        {
            var majors = await GetMajorsAsync(topic.Requirements.Select(r => r.MajorId).ToArray(), ct);
            foreach (var requirement in topic.Requirements) requirement.DepartmentId = majors.Single(m => m.Id == requirement.MajorId).DepartmentId;
            topic.PublishedBy = actor.Id; topic.PublishedAt = now;
        }
        else { topic.ClosedBy = actor.Id; topic.ClosedAt = now; topic.CloseReason = reason?.Trim(); }
        topic.ConcurrencyToken = Guid.NewGuid(); topic.UpdatedBy = actor.Id; topic.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, actor, false, ct))!;
    }

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        // Keep academic-policy reads stable through publication and include audit in the same commit.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await action();
            await tx.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Topic changed. Reload it before retrying."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new ConflictException("Topic code already exists in this project period."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        { throw new ConflictException("Topic academic references or constraints have changed."); }
        catch (Exception ex) when (LockConflict(ex))
        { throw new ConflictException("Topic or academic policy is being changed. Reload before retrying."); }
    }

    private static bool LockConflict(Exception error)
    {
        for (Exception? e = error; e is not null; e = e.InnerException)
            if (e is SqlException { Number: 1205 or 1222 }) return true;
        return false;
    }
}
