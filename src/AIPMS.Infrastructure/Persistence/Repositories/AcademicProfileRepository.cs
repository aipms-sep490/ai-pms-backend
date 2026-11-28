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
using AIPMS.Infrastructure.Services.Projects;
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
            if (ex is DbUpdateConcurrencyException || ex is SqlException { Number: 1205 or 1222 }) throw new ConflictException("Profile changed concurrently. Retry.");
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
        u.AcademicProfileReviewedBy, u.AcademicProfileReviewedAt, u.AcademicProfileRejectionReason, null,
        u.ClassCode, u.SpecializationId, u.Specialization == null ? null : u.Specialization.Code,
        u.Specialization == null ? null : u.Specialization.Name, u.CurriculumCode);

    public Task<AcademicProfileDto?> GetAsync(long userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student))
            .Select(Projection).SingleOrDefaultAsync(ct);

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
        if (status == "REJECTED")
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
                throw new ValidationException(new Dictionary<string, string[]> { ["reason"] = ["A reason of 1 to 2000 characters is required."] });
            var inActiveProject = await db.TeamMembers.AsNoTracking()
                .AnyAsync(m => m.UserId == userId
                    && m.LeftAt == null
                    && db.Projects.Any(p => p.TeamId == m.TeamId && (p.Status == "ACTIVE" || p.Status == "FINAL_SUBMISSION")), ct);
            if (inActiveProject)
            {
                throw new ConflictException(
                    "Cannot reject academic profile of a student currently in an ACTIVE or FINAL_SUBMISSION project.",
                    WorkflowErrorCodes.StudentInActiveProject);
            }
        }
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

    public async Task<AcademicProfileDto> UpdateAcademicScopeAsync(long userId, long? departmentId, long? majorId,
        string expectedToken, long actorId, DateTime now, CancellationToken ct = default)
    {
        await LockAsync(userId, ct);
        var managedDepartment = await GetReviewerDepartmentAsync(actorId, ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new NotFoundException("User", userId);
        if (managedDepartment.HasValue && (user.DepartmentId != managedDepartment || departmentId != managedDepartment))
            throw new ForbiddenException("Staff may only update profiles within their persisted department.");
        if (string.IsNullOrEmpty(expectedToken) || expectedToken != Convert.ToBase64String(user.RowVersion))
            throw new ConflictException("The academic profile changed. Reload before retrying.", WorkflowErrorCodes.StaleConcurrencyToken);
        if (departmentId.HasValue)
        {
            var department = await db.Departments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == departmentId.Value && d.IsActive && d.Organization.IsActive, ct)
                ?? throw new ValidationException(new Dictionary<string, string[]> { ["departmentId"] = ["The department is invalid or inactive."] });
            if (majorId.HasValue && !await db.Majors.AnyAsync(m => m.Id == majorId.Value && m.DepartmentId == department.Id && m.IsActive, ct))
                throw new ValidationException(new Dictionary<string, string[]> { ["majorId"] = ["The major does not belong to the selected department."] });
        }
        else if (majorId.HasValue)
            throw new ValidationException(new Dictionary<string, string[]> { ["departmentId"] = ["A department is required when a major is selected."] });
        var changed = user.DepartmentId != departmentId || user.MajorId != majorId;
        if (changed && (await db.TeamMembers.AnyAsync(m => m.UserId == userId && m.LeftAt == null && m.Team.Status != "DISBANDED", ct)
            || await db.SupervisorAssignments.AnyAsync(a => a.SupervisorProfile.UserId == userId && a.EndedAt == null, ct)
            || await db.Set<AIPMS.Infrastructure.Persistence.Models.EvaluationAssignment>().AnyAsync(a => a.EvaluatorId == userId && a.Status == "ACTIVE", ct)))
            throw new ConflictException("An active team, supervisor or evaluator obligation prevents changing this academic scope.");
        if (changed)
        {
            var previousDepartmentId = user.DepartmentId;
            var keepVerified = string.Equals(user.AcademicProfileStatus, "VERIFIED", StringComparison.Ordinal)
                && previousDepartmentId.HasValue
                && departmentId.HasValue
                && previousDepartmentId.Value == departmentId.Value
                && majorId.HasValue;

            user.DepartmentId = departmentId; user.MajorId = majorId;
            if (user.SpecializationId.HasValue)
            {
                var specStillValid = await db.DepartmentSpecializations.AsNoTracking().AnyAsync(
                    s => s.Id == user.SpecializationId.Value
                        && departmentId.HasValue && s.DepartmentId == departmentId.Value
                        && (s.MajorId == null || s.MajorId == majorId), ct);
                if (!specStillValid) user.SpecializationId = null;
            }
            if (!keepVerified)
            {
                user.AcademicProfileStatus = "PENDING";
                user.AcademicProfileReviewedBy = null;
                user.AcademicProfileReviewedAt = null;
                user.AcademicProfileRejectionReason = null;
            }
            user.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        var result = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(Projection)
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("User", userId);
        return result with { ConcurrencyToken = Convert.ToBase64String(user.RowVersion) };
    }

    public Task<bool> IsVerifiedAsync(long userId, CancellationToken ct = default)
        => db.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.AcademicProfileStatus == "VERIFIED", ct);

    public async Task<BulkVerifyAcademicProfilesResultDto> BulkVerifyAsync(
        long actorId,
        IReadOnlyList<long>? userIds,
        string? classCode,
        long? majorId,
        DateTime now,
        CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Bulk profile verification requires a transaction.");

        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Admin),
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff)
                    && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
            })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();

        if (!actor.IsStaff || !actor.DepartmentId.HasValue)
        {
            throw new ForbiddenException(
                actor.IsAdmin
                    ? "Platform administrators without DEPARTMENT_STAFF role cannot verify student academic profiles."
                    : "An active department staff account is required to bulk verify student academic profiles.",
                actor.IsAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : null);
        }

        var departmentId = actor.DepartmentId.Value;
        var hasExplicitIds = userIds is { Count: > 0 };
        var normalizedClass = string.IsNullOrWhiteSpace(classCode) ? null : classCode.Trim().ToUpperInvariant();
        if (!hasExplicitIds && normalizedClass is null && !majorId.HasValue)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["filter"] = ["Provide userIds, classCode, or majorId to bulk verify academic profiles."]
            });
        }

        long[] targetIds;
        if (hasExplicitIds)
        {
            targetIds = userIds!.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
            if (targetIds.Length == 0 || targetIds.Length > 500)
                throw new ValidationException(new Dictionary<string, string[]> { ["userIds"] = ["Bulk verify supports between 1 and 500 students per batch."] });

            var outsideDept = await db.Users.AsNoTracking()
                .AnyAsync(u => targetIds.Contains(u.Id) && u.DepartmentId != departmentId, ct);
            if (outsideDept)
                throw new ForbiddenException("Bulk verification can only target students within your assigned department.", WorkflowErrorCodes.StudentOutsideDepartment);
        }
        else
        {
            var query = db.Users.AsNoTracking()
                .Where(u => u.DepartmentId == departmentId
                    && u.Status == "ACTIVE"
                    && u.AcademicProfileStatus != "VERIFIED"
                    && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student));
            if (normalizedClass is not null)
                query = query.Where(u => u.ClassCode != null && u.ClassCode.ToUpper() == normalizedClass);
            if (majorId.HasValue)
                query = query.Where(u => u.MajorId == majorId.Value);

            targetIds = await query.OrderBy(u => u.Id).Select(u => u.Id).Take(501).ToArrayAsync(ct);
            if (targetIds.Length > 500)
                throw new ValidationException(new Dictionary<string, string[]> { ["filter"] = ["More than 500 matching students found. Narrow the filter (class/major) or select <= 500 students."] });
        }

        if (targetIds.Length == 0)
            return new BulkVerifyAcademicProfilesResultDto(0, 0, [], []);

        // Lock rows in ascending UserId order to prevent SQL Server deadlocks.
        foreach (var id in targetIds)
        {
            await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.users WITH (UPDLOCK, HOLDLOCK) WHERE id = {id}")
                .AsNoTracking().ToListAsync(ct);
        }

        var users = await db.Users
            .Include(u => u.Major)
            .Include(u => u.Department).ThenInclude(d => d!.Organization)
            .Where(u => targetIds.Contains(u.Id) && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student))
            .OrderBy(u => u.Id)
            .ToListAsync(ct);

        var verifiedIds = new List<long>();
        var skippedIds = new List<long>();
        var foundSet = new HashSet<long>();
        foreach (var user in users)
        {
            foundSet.Add(user.Id);
            var eligible = user.Status == "ACTIVE"
                && user.DepartmentId == departmentId
                && user.Major is not null && user.Major.IsActive
                && user.Department is not null && user.Department.IsActive && user.Department.Organization.IsActive
                && user.Major.DepartmentId == user.DepartmentId
                && !string.Equals(user.AcademicProfileStatus, "VERIFIED", StringComparison.Ordinal);

            if (!eligible)
            {
                skippedIds.Add(user.Id);
                continue;
            }

            user.AcademicProfileStatus = "VERIFIED";
            user.AcademicProfileReviewedBy = actorId;
            user.AcademicProfileReviewedAt = now;
            user.AcademicProfileRejectionReason = null;
            user.UpdatedAt = now;
            db.AcademicProfileVerifications.Add(new()
            {
                UserId = user.Id,
                Status = "VERIFIED",
                ReviewedBy = actorId,
                ReviewedAt = now,
                RejectionReason = null
            });
            verifiedIds.Add(user.Id);
        }

        foreach (var id in targetIds)
        {
            if (!foundSet.Contains(id)) skippedIds.Add(id);
        }

        if (verifiedIds.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await V4WorkflowNotificationHelper.EmitAsync(
                db,
                actorId,
                "ACADEMIC_PROFILE_VERIFIED",
                "Hồ sơ học vụ của bạn đã được xác minh",
                "Giáo vụ Khoa đã xác minh hồ sơ học vụ của bạn. Bạn đã đủ điều kiện tham gia lập nhóm và đăng ký đồ án theo quy định kỳ hiện hành.",
                "USER",
                actorId,
                verifiedIds,
                now,
                enqueueEmail: false,
                ct);
        }

        return new BulkVerifyAcademicProfilesResultDto(verifiedIds.Count, skippedIds.Count, verifiedIds, skippedIds);
    }
}
