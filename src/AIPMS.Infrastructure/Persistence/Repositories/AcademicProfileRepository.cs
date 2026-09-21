using System.Threading;
using System.Threading.Tasks;
using System.Data;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using Microsoft.Data.SqlClient;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class AcademicProfileRepository(AipmsDbContext db) : IAcademicProfileRepository
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try { var result = await action(ct); await tx.CommitAsync(ct); return result; }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is SqlException { Number: 1205 or 1222 }) throw new ConflictException("Profile changed concurrently. Retry.");
            throw;
        }
    }

    public async Task LockAsync(long userId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Profile review requires a transaction.");
        await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.users WITH (UPDLOCK, HOLDLOCK) WHERE id = {userId}")
            .AsNoTracking().ToListAsync(ct);
    }

    public async Task<long?> GetReviewerDepartmentAsync(long actorId, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().Where(u => u.Id == actorId && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, Admin = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Admin),
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff)
                    && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive }).SingleOrDefaultAsync(ct);
        if (actor is null || (!actor.Admin && !actor.Staff)) throw new ForbiddenException("An active administrator or department staff account is required.");
        return actor.Admin ? null : actor.DepartmentId;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Generated.Models.User, AcademicProfileDto>> Projection = u => new AcademicProfileDto(
        u.Id, u.FullName, u.Email, u.StudentCode, u.DepartmentId, u.Department == null ? null : u.Department.Name,
        u.MajorId, u.Major == null ? null : u.Major.Name, u.AcademicProfileStatus ?? "PENDING",
        u.AcademicProfileReviewedBy, u.AcademicProfileReviewedAt, u.AcademicProfileRejectionReason);

    public Task<AcademicProfileDto?> GetAsync(long userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student))
            .Select(u => new AcademicProfileDto(u.Id, u.FullName, u.Email, u.StudentCode, u.DepartmentId,
                u.Department == null ? null : u.Department.Name, u.MajorId, u.Major == null ? null : u.Major.Name,
                u.AcademicProfileStatus ?? "PENDING", u.AcademicProfileReviewedBy, u.AcademicProfileReviewedAt,
                u.AcademicProfileRejectionReason)).SingleOrDefaultAsync(ct);

    public async Task<PagedResult<AcademicProfileDto>> SearchAsync(string? status, long? departmentId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking().Where(u => u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.AcademicProfileStatus == status.Trim().ToUpperInvariant());
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId.Value);
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(Projection).ToListAsync(ct);
        return new(items, page, pageSize, count);
    }

    public async Task<AcademicProfileDto> SetStatusAsync(long userId, string status, long reviewerId, string? reason, DateTime now, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Profile review requires a transaction.");
        var departmentId = await GetReviewerDepartmentAsync(reviewerId, ct);
        var user = await db.Users.Include(u => u.Major).Include(u => u.Department).ThenInclude(d => d!.Organization)
            .SingleOrDefaultAsync(x => x.Id == userId && x.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student), ct)
            ?? throw new AIPMS.Application.Common.Exceptions.NotFoundException("User", userId);
        if (departmentId.HasValue && departmentId != user.DepartmentId) throw new ForbiddenException("The student is outside your department.");
        if (status == "VERIFIED" && (user.Status != "ACTIVE" || user.Major is null || !user.Major.IsActive
            || user.Department is null || !user.Department.IsActive || !user.Department.Organization.IsActive
            || user.Major.DepartmentId != user.DepartmentId))
            throw new ConflictException("Verification requires an active student, major, department and organization with matching scope.");
        if (status == "REJECTED" && (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000))
            throw new ValidationException(new Dictionary<string, string[]> { ["reason"] = ["A reason of 1 to 2000 characters is required."] });
        user.AcademicProfileStatus = status;
        user.AcademicProfileReviewedBy = reviewerId;
        user.AcademicProfileReviewedAt = now;
        user.AcademicProfileRejectionReason = reason;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        db.AcademicProfileVerifications.Add(new() { UserId = userId, Status = status, ReviewedBy = reviewerId,
            ReviewedAt = now, RejectionReason = reason });
        await db.SaveChangesAsync(ct);
        return (await GetAsync(userId, ct))!;
    }

    public Task<bool> IsVerifiedAsync(long userId, CancellationToken ct = default)
        => db.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.AcademicProfileStatus == "VERIFIED", ct);
}
