using System.Data;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.DepartmentStudents;
using AIPMS.Application.Features.DepartmentStudents.Abstractions;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.DepartmentStudents;

internal sealed partial class DepartmentStudentService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IDepartmentStudentService
{
    private const int MaxExportRows = 10_000;

    private long ActorId => currentUser.IsAuthenticated && currentUser.UserId is long id
        ? id
        : throw new UnauthorizedException();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<(long UserId, long DepartmentId)> RequireDepartmentStaffAsync(CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == ActorId && u.Status == "ACTIVE"
                && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff)
                && u.DepartmentId != null && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive)
            .Select(u => new { u.Id, DepartmentId = u.DepartmentId!.Value })
            .SingleOrDefaultAsync(ct);

        if (actor is null)
            throw new ForbiddenException("Only active department staff with an assigned department can manage department students.", WorkflowErrorCodes.ScopeAccessDenied);

        return (actor.Id, actor.DepartmentId);
    }

    private async Task<long> RequireAdminAsync(CancellationToken ct)
    {
        var isAdmin = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == ActorId && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Admin), ct);
        if (!isAdmin)
            throw new ForbiddenException("Only active administrators can perform this operation.");
        return ActorId;
    }

    private static readonly Regex ClassCodePattern = new("^[A-Z0-9][A-Z0-9._ /-]{0,49}$", RegexOptions.Compiled);
    private static readonly Regex SpecCodePattern = new("^[A-Z0-9][A-Z0-9._-]{0,49}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ValidAccountStatuses = new(["ACTIVE", "INACTIVE", "SUSPENDED"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ValidAccountRoles = new([AppRoles.Student, AppRoles.Lecturer, AppRoles.DepartmentStaff, AppRoles.Admin], StringComparer.OrdinalIgnoreCase);

    private static string? NormalizeClassCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed.Length > 50 || trimmed.Any(char.IsControl) || !ClassCodePattern.IsMatch(trimmed))
            throw new ValidationException(new Dictionary<string, string[]> { ["classCode"] = ["Class code must start with a letter or digit and contain 1 to 50 valid characters (A-Z, 0-9, space, '.', '_', '-', '/')."] });
        return trimmed;
    }

    private static string NormalizeSpecCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ValidationException(new Dictionary<string, string[]> { ["code"] = ["Specialization code is required."] });
        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed.Length > 50 || trimmed.Any(char.IsControl) || !SpecCodePattern.IsMatch(trimmed))
            throw new ValidationException(new Dictionary<string, string[]> { ["code"] = ["Specialization code must start with a letter or digit and contain 1 to 50 valid characters (A-Z, 0-9, '.', '_', '-')."] });
        return trimmed;
    }

    private static string NormalizeSpecName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ValidationException(new Dictionary<string, string[]> { ["name"] = ["Specialization name is required."] });
        var trimmed = raw.Trim();
        if (trimmed.Length > 200 || trimmed.Any(char.IsControl))
            throw new ValidationException(new Dictionary<string, string[]> { ["name"] = ["Specialization name must be 1 to 200 valid characters."] });
        return trimmed;
    }

    private static string? NormalizeDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > 500 || trimmed.Any(char.IsControl))
            throw new ValidationException(new Dictionary<string, string[]> { ["description"] = ["Description must be at most 500 valid characters."] });
        return trimmed;
    }

    private static DepartmentSpecializationDto ToDto(DepartmentSpecialization spec) => new(
        spec.Id,
        spec.DepartmentId,
        spec.MajorId,
        spec.Major?.Code,
        spec.Major?.Name,
        spec.Code,
        spec.Name,
        spec.Description,
        spec.IsActive,
        spec.CreatedAt,
        spec.UpdatedAt);

    public async Task<IReadOnlyList<DepartmentSpecializationDto>> ListSpecializationsAsync(
        long? departmentId,
        long? majorId,
        bool includeInactive,
        CancellationToken ct = default)
    {
        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == ActorId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                u.MajorId,
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Admin),
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff),
                IsStudent = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student),
                IsLecturer = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer)
            })
            .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedException();

        long? effectiveDeptId = departmentId;
        long? effectiveMajorId = majorId;
        var effectiveIncludeInactive = includeInactive && (actor.IsAdmin || actor.IsStaff);

        if (actor.IsStaff && !actor.IsAdmin)
        {
            if (departmentId.HasValue && actor.DepartmentId != departmentId.Value)
                throw new ForbiddenException("Department staff can only view specializations within their own department.", WorkflowErrorCodes.ScopeAccessDenied);
            effectiveDeptId = actor.DepartmentId;
        }
        else if (!actor.IsAdmin && !actor.IsLecturer)
        {
            effectiveDeptId = actor.DepartmentId;
            effectiveMajorId ??= actor.MajorId;
        }

        var query = db.DepartmentSpecializations.AsNoTracking().Include(s => s.Major).AsQueryable();
        if (effectiveDeptId.HasValue)
            query = query.Where(s => s.DepartmentId == effectiveDeptId.Value);
        if (effectiveMajorId.HasValue)
            query = query.Where(s => s.MajorId == null || s.MajorId == effectiveMajorId.Value);
        if (!effectiveIncludeInactive)
            query = query.Where(s => s.IsActive);

        var items = await query.OrderBy(s => s.Name).ThenBy(s => s.Code).ToListAsync(ct);
        return items.Select(ToDto).ToArray();
    }

    public async Task<DepartmentSpecializationDto> CreateSpecializationAsync(
        CreateDepartmentSpecializationRequest request,
        CancellationToken ct = default)
    {
        var (actorId, deptId) = await RequireDepartmentStaffAsync(ct);
        var code = NormalizeSpecCode(request.Code);
        var name = NormalizeSpecName(request.Name);
        var desc = NormalizeDescription(request.Description);

        if (request.MajorId.HasValue)
        {
            var validMajor = await db.Majors.AsNoTracking()
                .AnyAsync(m => m.Id == request.MajorId.Value && m.DepartmentId == deptId && m.IsActive, ct);
            if (!validMajor)
                throw new DomainException("Major does not belong to your department or is inactive.", WorkflowErrorCodes.InvalidSpecializationForScope);
        }

        if (await db.DepartmentSpecializations.AsNoTracking().AnyAsync(s => s.DepartmentId == deptId && s.Code == code, ct))
            throw new ConflictException($"Specialization code '{code}' already exists in your department.");

        var now = Now;
        var entity = new DepartmentSpecialization
        {
            DepartmentId = deptId,
            MajorId = request.MajorId,
            Code = code,
            Name = name,
            Description = desc,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.DepartmentSpecializations.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(actorId, "DEPARTMENT_SPECIALIZATION_CREATED", "DEPARTMENT_SPECIALIZATION", entity.Id,
            new Dictionary<string, object?> { ["departmentId"] = deptId, ["majorId"] = entity.MajorId, ["code"] = entity.Code, ["name"] = entity.Name }), ct);

        var loaded = await db.DepartmentSpecializations.AsNoTracking().Include(s => s.Major).SingleAsync(s => s.Id == entity.Id, ct);
        return ToDto(loaded);
    }

    public async Task<DepartmentSpecializationDto> UpdateSpecializationAsync(
        long specializationId,
        UpdateDepartmentSpecializationRequest request,
        CancellationToken ct = default)
    {
        var (actorId, deptId) = await RequireDepartmentStaffAsync(ct);
        var entity = await db.DepartmentSpecializations.SingleOrDefaultAsync(s => s.Id == specializationId, ct)
            ?? throw new NotFoundException("DepartmentSpecialization", specializationId);
        if (entity.DepartmentId != deptId)
            throw new ForbiddenException("Cannot edit a specialization belonging to another department.", WorkflowErrorCodes.ScopeAccessDenied);

        var code = NormalizeSpecCode(request.Code);
        var name = NormalizeSpecName(request.Name);
        var desc = NormalizeDescription(request.Description);

        if (request.MajorId.HasValue)
        {
            var validMajor = await db.Majors.AsNoTracking()
                .AnyAsync(m => m.Id == request.MajorId.Value && m.DepartmentId == deptId && m.IsActive, ct);
            if (!validMajor)
                throw new DomainException("Major does not belong to your department or is inactive.", WorkflowErrorCodes.InvalidSpecializationForScope);
        }

        if (await db.DepartmentSpecializations.AsNoTracking().AnyAsync(s => s.DepartmentId == deptId && s.Code == code && s.Id != specializationId, ct))
            throw new ConflictException($"Specialization code '{code}' already exists in your department.");

        if (request.MajorId.HasValue && request.MajorId != entity.MajorId)
        {
            var hasConflictingStudents = await db.Users.AsNoTracking()
                .AnyAsync(u => u.SpecializationId == specializationId && u.MajorId != request.MajorId.Value, ct);
            if (hasConflictingStudents)
                throw new ConflictException("Cannot restrict specialization to another major while students from a different major are assigned to it.");
        }

        entity.MajorId = request.MajorId;
        entity.Code = code;
        entity.Name = name;
        entity.Description = desc;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(actorId, "DEPARTMENT_SPECIALIZATION_UPDATED", "DEPARTMENT_SPECIALIZATION", entity.Id,
            new Dictionary<string, object?> { ["departmentId"] = deptId, ["code"] = entity.Code, ["isActive"] = entity.IsActive }), ct);

        var loaded = await db.DepartmentSpecializations.AsNoTracking().Include(s => s.Major).SingleAsync(s => s.Id == entity.Id, ct);
        return ToDto(loaded);
    }

    public async Task<AcademicProfileDto> UpdateMyStudentAcademicAsync(
        UpdateMyStudentAcademicRequest request,
        CancellationToken ct = default)
    {
        var classCode = NormalizeClassCode(request.ClassCode);
        if (string.IsNullOrWhiteSpace(classCode))
            throw new ValidationException(new Dictionary<string, string[]> { ["classCode"] = ["Class code is required."] });

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users
            .Include(u => u.Department)
            .Include(u => u.Major)
            .Include(u => u.Specialization)
            .Include(u => u.UserRoleUsers).ThenInclude(r => r.Role)
            .SingleOrDefaultAsync(u => u.Id == ActorId && u.Status == "ACTIVE", ct)
            ?? throw new UnauthorizedException();

        if (!user.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student))
            throw new ForbiddenException("Only student accounts can self-update student class and specialization.");

        var currentStatus = (user.AcademicProfileStatus ?? "PENDING").ToUpperInvariant();
        var inLockedTeam = await db.TeamMembers.AsNoTracking()
            .AnyAsync(m => m.UserId == user.Id && m.LeftAt == null && m.Team.Status != "DISBANDED", ct);
        if (currentStatus == "VERIFIED" || inLockedTeam)
            throw new ConflictException(
                "Academic profile is already VERIFIED or bound to an active team; contact your department staff to update class or specialization.",
                WorkflowErrorCodes.AcademicProfileLocked);

        if (request.SpecializationId.HasValue)
        {
            var spec = await db.DepartmentSpecializations.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == request.SpecializationId.Value && s.IsActive, ct);
            if (spec is null || !user.DepartmentId.HasValue || spec.DepartmentId != user.DepartmentId.Value
                || (spec.MajorId.HasValue && user.MajorId != spec.MajorId))
            {
                throw new DomainException(
                    "Selected specialization does not belong to your active department/major.",
                    WorkflowErrorCodes.InvalidSpecializationForScope);
            }
        }

        user.ClassCode = classCode;
        user.SpecializationId = request.SpecializationId;
        if (currentStatus == "REJECTED")
        {
            user.AcademicProfileStatus = "PENDING";
            user.AcademicProfileRejectionReason = null;
        }
        user.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(user.Id, "STUDENT_SELF_ACADEMIC_UPDATED", "USER", user.Id,
            new Dictionary<string, object?>
            {
                ["classCode"] = user.ClassCode,
                ["specializationId"] = user.SpecializationId,
                ["status"] = user.AcademicProfileStatus
            }), ct);

        await tx.CommitAsync(ct);

        var refreshed = await db.Users.AsNoTracking()
            .Include(u => u.Department).Include(u => u.Major).Include(u => u.Specialization)
            .SingleAsync(u => u.Id == user.Id, ct);

        return new AcademicProfileDto(
            refreshed.Id, refreshed.FullName, refreshed.Email, refreshed.StudentCode,
            refreshed.DepartmentId, refreshed.Department?.Name,
            refreshed.MajorId, refreshed.Major?.Name,
            refreshed.AcademicProfileStatus ?? "PENDING",
            refreshed.AcademicProfileReviewedBy, refreshed.AcademicProfileReviewedAt, refreshed.AcademicProfileRejectionReason,
            Convert.ToBase64String(refreshed.RowVersion),
            refreshed.ClassCode, refreshed.SpecializationId, refreshed.Specialization?.Code, refreshed.Specialization?.Name,
            refreshed.CurriculumCode);
    }

    public async Task<RosterFile> ExportAdminUserAccountsAsync(
        AdminUserAccountExportQuery query,
        CancellationToken ct = default)
    {
        var actorId = await RequireAdminAsync(ct);
        var errors = new Dictionary<string, string[]>();
        if (!string.IsNullOrWhiteSpace(query.Status) && !ValidAccountStatuses.Contains(query.Status.Trim()))
            errors["status"] = ["Status must be ACTIVE, INACTIVE, or SUSPENDED."];
        if (!string.IsNullOrWhiteSpace(query.Role) && !ValidAccountRoles.Contains(query.Role.Trim()))
            errors["role"] = ["Role must be STUDENT, LECTURER, DEPARTMENT_STAFF, or ADMIN."];
        if (query.DepartmentId.HasValue && query.DepartmentId.Value <= 0)
            errors["departmentId"] = ["Department ID must be positive."];
        if (query.MajorId.HasValue && query.MajorId.Value <= 0)
            errors["majorId"] = ["Major ID must be positive."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var usersQuery = db.Users.AsNoTracking()
            .Include(u => u.Department)
            .Include(u => u.Major)
            .Include(u => u.Specialization)
            .Include(u => u.UserRoleUsers).ThenInclude(ur => ur.Role)
            .AsQueryable();

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            usersQuery = usersQuery.Where(u =>
                u.Email.Contains(search)
                || u.FullName.Contains(search)
                || (u.StudentCode != null && u.StudentCode.Contains(search))
                || (u.EmployeeCode != null && u.EmployeeCode.Contains(search))
                || (u.ClassCode != null && u.ClassCode.Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToUpperInvariant();
            usersQuery = usersQuery.Where(u => u.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim().ToUpperInvariant();
            usersQuery = usersQuery.Where(u => u.UserRoleUsers.Any(ur => ur.Role.Code == role));
        }
        if (query.DepartmentId.HasValue)
            usersQuery = usersQuery.Where(u => u.DepartmentId == query.DepartmentId.Value);
        if (query.MajorId.HasValue)
            usersQuery = usersQuery.Where(u => u.MajorId == query.MajorId.Value);

        var rows = await usersQuery
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Take(MaxExportRows + 1)
            .Select(u => new AdminUserAccountExportRow(
                u.StudentCode ?? u.EmployeeCode,
                u.FullName,
                u.Email,
                u.Phone,
                string.Join(", ", u.UserRoleUsers.Select(r => r.Role.Code).OrderBy(c => c)),
                u.Department == null ? null : u.Department.Name,
                u.Major == null ? null : u.Major.Name,
                u.ClassCode,
                u.Specialization == null ? null : u.Specialization.Name,
                u.CurriculumCode,
                u.Status,
                u.AcademicProfileStatus ?? "PENDING",
                u.LastLoginAt))
            .ToListAsync(ct);

        if (rows.Count > MaxExportRows)
            throw new DomainException("ACCOUNT_EXPORT_ROW_LIMIT_EXCEEDED: narrow filters to at most 10000 accounts.");

        var bytes = DepartmentStudentWorkbook.CreateAdminAccountsWorkbook(rows);
        await audit.RecordAsync(new AuditEntry(actorId, "USER_ACCOUNTS_EXPORTED", "USER", null,
            new Dictionary<string, object?>
            {
                ["role"] = query.Role,
                ["departmentId"] = query.DepartmentId,
                ["majorId"] = query.MajorId,
                ["status"] = query.Status,
                ["rowCount"] = rows.Count
            }), ct);

        return new RosterFile(bytes, $"user-accounts-{Now:yyyyMMdd}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}
