using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using SupervisorProfile = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorProfile;
using SupervisorExpertise = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorExpertise;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class SupervisorProfileRepository(AipmsDbContext context) : ISupervisorProfileRepository
{
    public async Task<SupervisorAccount?> GetAccountAsync(long userId, CancellationToken ct)
    {
        var user = await context.Users.AsNoTracking().Include(u => u.Department).ThenInclude(d => d!.Organization)
            .Include(u => u.UserRoleUsers).ThenInclude(r => r.Role).SingleOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : new(user.Id, user.DepartmentId, user.Status == "ACTIVE",
            user.Department is { IsActive: true, Organization.IsActive: true },
            user.UserRoleUsers.Select(r => r.Role.Code).ToArray());
    }

    private IQueryable<SupervisorProfile> DirectoryQuery() => context.SupervisorProfiles.AsNoTracking()
        .Where(p => p.User.Status == "ACTIVE" && p.User.Department != null
            && p.User.Department.IsActive && p.User.Department.Organization.IsActive
            && p.User.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer))
        .Include(p => p.User).ThenInclude(u => u.Department).Include(p => p.SupervisorExpertises);

    public async Task<PagedResult<SupervisorProfileModel>> SearchAsync(SupervisorSearch search, CancellationToken ct)
    {
        var query = DirectoryQuery();
        if (search.DepartmentId.HasValue) query = query.Where(p => p.User.DepartmentId == search.DepartmentId);
        if (!string.IsNullOrWhiteSpace(search.Search)) query = query.Where(p => p.User.FullName.Contains(search.Search));
        if (!string.IsNullOrWhiteSpace(search.Expertise))
            query = query.Where(p => p.SupervisorExpertises.Any(e => e.ExpertiseName.Contains(search.Expertise)));
        if (search.IsAvailable.HasValue) query = query.Where(p => p.IsAvailable == search.IsAvailable);
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderBy(p => p.User.FullName).ThenBy(p => p.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(ct);
        return new(items.Select(p => p.ToApplication()).ToArray(), search.Page, search.PageSize, count);
    }

    public async Task<SupervisorProfileModel?> GetAsync(long profileId, CancellationToken ct) =>
        (await DirectoryQuery().SingleOrDefaultAsync(p => p.Id == profileId, ct))?.ToApplication();

    public async Task<SupervisorProfileModel?> GetByUserAsync(long userId, CancellationToken ct) =>
        (await DirectoryQuery().SingleOrDefaultAsync(p => p.UserId == userId, ct))?.ToApplication();

    public async Task<SupervisorProfileModel> UpsertAsync(long userId, string? bio, bool available, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var profile = await context.SupervisorProfiles.SingleOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new SupervisorProfile { UserId = userId, CreatedAt = now };
            context.SupervisorProfiles.Add(profile);
        }
        // Capacity belongs to the later assignment/policy slice; do not overwrite it here.
        profile.Bio = bio;
        profile.IsAvailable = available;
        profile.UpdatedAt = now;
        await context.SaveChangesAsync(ct);
        return (await GetAsync(profile.Id, ct))!;
    }

    public async Task<SupervisorProfileModel> ReplaceExpertiseAsync(long profileId,
        IReadOnlyList<SupervisorExpertiseModel> expertise, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var profile = await context.SupervisorProfiles.Include(p => p.SupervisorExpertises)
            .SingleOrDefaultAsync(p => p.Id == profileId, ct) ?? throw new NotFoundException("SupervisorProfile", profileId);
        // Delete first to avoid unique-index collisions when names differ only by DB collation.
        context.SupervisorExpertises.RemoveRange(profile.SupervisorExpertises);
        await context.SaveChangesAsync(ct);
        context.SupervisorExpertises.AddRange(expertise.Select(e => new SupervisorExpertise
        {
            SupervisorProfileId = profileId, ExpertiseName = e.Name, ProficiencyLevel = e.ProficiencyLevel,
            CreatedAt = now, UpdatedAt = now
        }));
        profile.UpdatedAt = now;
        await context.SaveChangesAsync(ct);
        return (await GetAsync(profileId, ct))!;
    }

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is not null) return await action();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await action();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            if (IsWriteConflict(ex))
                throw new ConflictException("The supervisor profile changed concurrently or contains duplicate expertise. Reload and retry.");
            throw;
        }
    }

    private static bool IsWriteConflict(Exception exception)
    {
        // EF Core can wrap a deadlock in InvalidOperationException around DbUpdateException.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 or 2601 or 2627 }) return true;
        return false;
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Supervisor mutations require a transaction including access checks and audit.");
    }
}
