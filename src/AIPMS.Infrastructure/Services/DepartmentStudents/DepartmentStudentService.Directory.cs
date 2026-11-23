using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.DepartmentStudents;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.DepartmentStudents;

internal sealed partial class DepartmentStudentService
{
    private static readonly HashSet<string> ValidProfileStatuses = new(["PENDING", "VERIFIED", "REJECTED"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ValidQualificationStatuses = new(["NOT_SUBMITTED", "PENDING_VERIFICATION", "VERIFIED", "REJECTED", "EXPIRED"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ValidTeamStatuses = new(["IN_TEAM", "WITHOUT_TEAM", "NO_TEAM"], StringComparer.OrdinalIgnoreCase);

    private static void ValidateDirectoryQuery(DepartmentStudentQuery query)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.SemesterId.HasValue && query.SemesterId.Value <= 0)
            errors["semesterId"] = ["Semester ID must be positive."];
        if (query.MajorId.HasValue && query.MajorId.Value <= 0)
            errors["majorId"] = ["Major ID must be positive."];
        if (query.SpecializationId.HasValue && query.SpecializationId.Value <= 0)
            errors["specializationId"] = ["Specialization ID must be positive."];
        if (!string.IsNullOrWhiteSpace(query.ProfileStatus) && !ValidProfileStatuses.Contains(query.ProfileStatus.Trim()))
            errors["profileStatus"] = ["Profile status must be PENDING, VERIFIED, or REJECTED."];
        if (!string.IsNullOrWhiteSpace(query.QualificationStatus) && !ValidQualificationStatuses.Contains(query.QualificationStatus.Trim()))
            errors["qualificationStatus"] = ["Qualification status must be NOT_SUBMITTED, PENDING_VERIFICATION, VERIFIED, REJECTED, or EXPIRED."];
        if (!string.IsNullOrWhiteSpace(query.TeamStatus) && !ValidTeamStatuses.Contains(query.TeamStatus.Trim()))
            errors["teamStatus"] = ["Team status must be IN_TEAM or WITHOUT_TEAM."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private async Task<long?> ResolveEffectiveSemesterIdAsync(long? requestedSemesterId, long departmentId, CancellationToken ct)
    {
        if (requestedSemesterId.HasValue && requestedSemesterId.Value > 0)
            return requestedSemesterId.Value;

        var orgId = await db.Departments.AsNoTracking()
            .Where(d => d.Id == departmentId)
            .Select(d => d.OrganizationId)
            .SingleAsync(ct);

        return await db.AcademicSemesters.AsNoTracking()
            .Where(s => s.OrganizationId == orgId && s.Status == "ACTIVE")
            .OrderByDescending(s => s.StartDate).ThenByDescending(s => s.Id)
            .Select(s => (long?)s.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<(IReadOnlyList<DepartmentStudentSummaryDto> Filtered, DepartmentStudentDirectoryStatsDto Stats)>
        LoadDepartmentStudentRowsAsync(long deptId, DepartmentStudentQuery query, CancellationToken ct)
    {
        ValidateDirectoryQuery(query);
        var semesterId = await ResolveEffectiveSemesterIdAsync(query.SemesterId, deptId, ct);

        var students = await db.Users.AsNoTracking()
            .Where(u => u.DepartmentId == deptId && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student))
            .Select(u => new
            {
                u.Id,
                u.StudentCode,
                u.FullName,
                u.Email,
                u.Phone,
                u.Status,
                u.DepartmentId,
                DepartmentName = u.Department == null ? null : u.Department.Name,
                u.MajorId,
                MajorCode = u.Major == null ? null : u.Major.Code,
                MajorName = u.Major == null ? null : u.Major.Name,
                u.ClassCode,
                u.SpecializationId,
                SpecializationCode = u.Specialization == null ? null : u.Specialization.Code,
                SpecializationName = u.Specialization == null ? null : u.Specialization.Name,
                u.CurriculumCode,
                AcademicProfileStatus = u.AcademicProfileStatus ?? "PENDING",
                u.LastLoginAt
            })
            .OrderBy(u => u.StudentCode ?? u.Email).ThenBy(u => u.Id)
            .ToListAsync(ct);

        var userIds = students.Select(s => s.Id).ToArray();

        var qualByUser = userIds.Length == 0
            ? new Dictionary<long, string>()
            : (await db.Set<StudentQualification>().AsNoTracking()
                .Where(q => userIds.Contains(q.UserId))
                .OrderByDescending(q => q.UpdatedAt).ThenByDescending(q => q.Id)
                .Select(q => new { q.UserId, q.VerificationStatus })
                .ToListAsync(ct))
                .GroupBy(q => q.UserId)
                .ToDictionary(g => g.Key, g => g.First().VerificationStatus);

        var teamMembersQuery = db.TeamMembers.AsNoTracking()
            .Where(m => userIds.Contains(m.UserId) && m.LeftAt == null && m.Team.Status != "DISBANDED");
        if (semesterId.HasValue)
            teamMembersQuery = teamMembersQuery.Where(m => m.Team.AcademicSemesterId == semesterId.Value);

        var teamMembers = userIds.Length == 0
            ? []
            : await teamMembersQuery
                .OrderByDescending(m => m.JoinedAt).ThenByDescending(m => m.Id)
                .Select(m => new { m.UserId, m.TeamId, TeamCode = m.Team.Code, m.IsLeader })
                .ToListAsync(ct);

        var teamByUser = teamMembers.GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => g.First());
        var teamIds = teamByUser.Values.Select(t => t.TeamId).Distinct().ToArray();
        var projectByTeam = teamIds.Length == 0
            ? new Dictionary<long, (long Id, string Code, string Status)>()
            : (await db.Projects.AsNoTracking()
                .Where(p => teamIds.Contains(p.TeamId))
                .OrderByDescending(p => p.Id)
                .Select(p => new { p.TeamId, p.Id, p.Code, p.Status })
                .ToListAsync(ct))
                .GroupBy(p => p.TeamId)
                .ToDictionary(g => g.Key, g => (g.First().Id, g.First().Code, g.First().Status));

        var allRows = students.Select(s =>
        {
            var qualStatus = qualByUser.TryGetValue(s.Id, out var q) ? q : "NOT_SUBMITTED";
            teamByUser.TryGetValue(s.Id, out var tm);
            (long Id, string Code, string Status)? proj = tm is not null && projectByTeam.TryGetValue(tm.TeamId, out var pr) ? pr : null;

            return new DepartmentStudentSummaryDto(
                s.Id, s.StudentCode, s.FullName, s.Email, s.Phone, s.Status,
                s.DepartmentId, s.DepartmentName, s.MajorId, s.MajorCode, s.MajorName,
                s.ClassCode, s.SpecializationId, s.SpecializationCode, s.SpecializationName,
                s.CurriculumCode, s.AcademicProfileStatus, qualStatus,
                tm?.TeamId, tm?.TeamCode, tm?.IsLeader ?? false,
                proj?.Id, proj?.Code, proj?.Status, s.LastLoginAt);
        }).ToList();

        var stats = new DepartmentStudentDirectoryStatsDto(
            TotalStudents: allRows.Count,
            PendingVerificationCount: allRows.Count(r => r.AcademicProfileStatus == "PENDING" || r.QualificationStatus == "PENDING_VERIFICATION"),
            QualifiedCount: allRows.Count(r => r.AcademicProfileStatus == "VERIFIED" && r.QualificationStatus == "VERIFIED"),
            WithoutTeamCount: allRows.Count(r => !r.TeamId.HasValue));

        IEnumerable<DepartmentStudentSummaryDto> filtered = allRows;
        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(r =>
                r.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (r.StudentCode != null && r.StudentCode.Contains(search, StringComparison.OrdinalIgnoreCase))
                || (r.ClassCode != null && r.ClassCode.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }
        if (query.MajorId.HasValue)
            filtered = filtered.Where(r => r.MajorId == query.MajorId.Value);
        if (query.SpecializationId.HasValue)
            filtered = filtered.Where(r => r.SpecializationId == query.SpecializationId.Value);
        if (!string.IsNullOrWhiteSpace(query.ClassCode))
        {
            var cls = query.ClassCode.Trim();
            filtered = filtered.Where(r => r.ClassCode != null && r.ClassCode.Contains(cls, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.CurriculumCode))
        {
            var cur = query.CurriculumCode.Trim();
            filtered = filtered.Where(r => r.CurriculumCode != null && r.CurriculumCode.Contains(cur, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.ProfileStatus))
        {
            var ps = query.ProfileStatus.Trim().ToUpperInvariant();
            filtered = filtered.Where(r => string.Equals(r.AcademicProfileStatus, ps, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.QualificationStatus))
        {
            var qs = query.QualificationStatus.Trim().ToUpperInvariant();
            filtered = filtered.Where(r => string.Equals(r.QualificationStatus, qs, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.TeamStatus))
        {
            var ts = query.TeamStatus.Trim().ToUpperInvariant();
            if (ts == "IN_TEAM") filtered = filtered.Where(r => r.TeamId.HasValue);
            else if (ts is "WITHOUT_TEAM" or "NO_TEAM") filtered = filtered.Where(r => !r.TeamId.HasValue);
        }

        return (filtered.ToArray(), stats);
    }

    public async Task<DepartmentStudentDirectoryPageDto> ListDepartmentStudentsAsync(
        DepartmentStudentQuery query,
        CancellationToken ct = default)
    {
        var (_, deptId) = await RequireDepartmentStaffAsync(ct);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var (filtered, stats) = await LoadDepartmentStudentRowsAsync(deptId, query, ct);
        var total = filtered.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new(items, page, pageSize, total, totalPages, stats);
    }

    public async Task<DepartmentStudentDetailDto> GetDepartmentStudentDetailAsync(
        long studentUserId,
        long? semesterId,
        CancellationToken ct = default)
    {
        var (_, deptId) = await RequireDepartmentStaffAsync(ct);
        var user = await db.Users.AsNoTracking()
            .Include(u => u.Department)
            .Include(u => u.Major)
            .Include(u => u.Specialization)
            .Include(u => u.UserRoleUsers).ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.Id == studentUserId && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student), ct)
            ?? throw new NotFoundException("Student", studentUserId);

        if (user.DepartmentId != deptId)
            throw new ForbiddenException("Student is outside your department scope.", WorkflowErrorCodes.ScopeAccessDenied);

        var effSemesterId = await ResolveEffectiveSemesterIdAsync(semesterId, deptId, ct);
        var qual = await db.Set<StudentQualification>().AsNoTracking()
            .Where(q => q.UserId == studentUserId)
            .OrderByDescending(q => q.UpdatedAt).ThenByDescending(q => q.Id)
            .FirstOrDefaultAsync(ct);

        var tmQuery = db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == studentUserId && m.LeftAt == null && m.Team.Status != "DISBANDED");
        if (effSemesterId.HasValue)
            tmQuery = tmQuery.Where(m => m.Team.AcademicSemesterId == effSemesterId.Value);

        var tm = await tmQuery.OrderByDescending(m => m.JoinedAt).ThenByDescending(m => m.Id)
            .Select(m => new { m.TeamId, TeamCode = m.Team.Code, m.IsLeader })
            .FirstOrDefaultAsync(ct);

        var proj = tm is null ? null : await db.Projects.AsNoTracking()
            .Where(p => p.TeamId == tm.TeamId)
            .OrderByDescending(p => p.Id)
            .Select(p => new { p.Id, p.Code, p.Title, p.Status, p.TrackCode })
            .FirstOrDefaultAsync(ct);

        var summary = new DepartmentStudentSummaryDto(
            user.Id, user.StudentCode, user.FullName, user.Email, user.Phone, user.Status,
            user.DepartmentId, user.Department?.Name, user.MajorId, user.Major?.Code, user.Major?.Name,
            user.ClassCode, user.SpecializationId, user.Specialization?.Code, user.Specialization?.Name,
            user.CurriculumCode, user.AcademicProfileStatus ?? "PENDING", qual?.VerificationStatus ?? "NOT_SUBMITTED",
            tm?.TeamId, tm?.TeamCode, tm?.IsLeader ?? false,
            proj?.Id, proj?.Code, proj?.Status, user.LastLoginAt);

        return new DepartmentStudentDetailDto(
            summary,
            user.AcademicProfileRejectionReason,
            user.AcademicProfileReviewedAt,
            user.AcademicProfileReviewedBy,
            qual?.Id,
            qual?.TrainingStatus,
            qual?.CertificateNumber,
            qual?.CertificateFileId,
            qual?.IssuedAt,
            qual?.ExpiresAt,
            qual?.RejectionReason,
            qual is null ? null : qual.ConcurrencyToken.ToString("N"),
            proj?.Title,
            proj?.TrackCode,
            Convert.ToBase64String(user.RowVersion));
    }

    public async Task<DepartmentStudentDetailDto> UpdateDepartmentStudentAcademicAsync(
        long studentUserId,
        UpdateDepartmentStudentAcademicRequest request,
        CancellationToken ct = default)
    {
        var (actorId, deptId) = await RequireDepartmentStaffAsync(ct);
        var classCode = NormalizeClassCode(request.ClassCode);
        var curriculumCode = string.IsNullOrWhiteSpace(request.CurriculumCode) ? null : request.CurriculumCode.Trim();
        if (curriculumCode is { Length: > 100 } || (curriculumCode is not null && curriculumCode.Any(char.IsControl)))
            throw new ValidationException(new Dictionary<string, string[]> { ["curriculumCode"] = ["Curriculum code must be at most 100 valid characters."] });

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users
            .Include(u => u.UserRoleUsers).ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.Id == studentUserId && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student), ct)
            ?? throw new NotFoundException("Student", studentUserId);

        if (user.DepartmentId != deptId)
            throw new ForbiddenException("Student is outside your department scope.", WorkflowErrorCodes.ScopeAccessDenied);

        if (string.IsNullOrEmpty(request.ConcurrencyToken) || request.ConcurrencyToken != Convert.ToBase64String(user.RowVersion))
            throw new ConflictException("Student profile changed concurrently. Reload and retry.", WorkflowErrorCodes.StaleConcurrencyToken);

        var targetMajorId = request.MajorId ?? user.MajorId;
        if (request.MajorId.HasValue)
        {
            var validMajor = await db.Majors.AsNoTracking()
                .AnyAsync(m => m.Id == request.MajorId.Value && m.DepartmentId == deptId && m.IsActive, ct);
            if (!validMajor)
                throw new DomainException("Major does not belong to your department or is inactive.", WorkflowErrorCodes.InvalidSpecializationForScope);
            if (user.MajorId != request.MajorId.Value && await db.TeamMembers.AsNoTracking()
                .AnyAsync(m => m.UserId == studentUserId && m.LeftAt == null && m.Team.Status != "DISBANDED", ct))
                throw new ConflictException("Cannot change major while the student belongs to an active team.");
        }

        if (request.SpecializationId.HasValue)
        {
            var spec = await db.DepartmentSpecializations.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == request.SpecializationId.Value && s.IsActive, ct);
            if (spec is null || spec.DepartmentId != deptId || (spec.MajorId.HasValue && targetMajorId != spec.MajorId))
                throw new DomainException(
                    "Selected specialization does not match your department and student major.",
                    WorkflowErrorCodes.InvalidSpecializationForScope);
        }

        user.MajorId = targetMajorId;
        user.ClassCode = classCode;
        user.SpecializationId = request.SpecializationId;
        user.CurriculumCode = curriculumCode;
        user.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(actorId, "DEPARTMENT_STUDENT_ACADEMIC_UPDATED", "USER", user.Id,
            new Dictionary<string, object?>
            {
                ["departmentId"] = deptId,
                ["majorId"] = user.MajorId,
                ["classCode"] = user.ClassCode,
                ["specializationId"] = user.SpecializationId,
                ["curriculumCode"] = user.CurriculumCode
            }), ct);

        await tx.CommitAsync(ct);
        return await GetDepartmentStudentDetailAsync(studentUserId, null, ct);
    }

    public async Task<RosterFile> ExportDepartmentStudentsAsync(
        DepartmentStudentQuery query,
        CancellationToken ct = default)
    {
        var (actorId, deptId) = await RequireDepartmentStaffAsync(ct);
        var (filtered, _) = await LoadDepartmentStudentRowsAsync(deptId, query, ct);
        if (filtered.Count > MaxExportRows)
            throw new DomainException("ROSTER_EXPORT_ROW_LIMIT_EXCEEDED: narrow filters to at most 10000 students.");

        var bytes = DepartmentStudentWorkbook.CreateDepartmentStudentsWorkbook(filtered);
        await audit.RecordAsync(new AuditEntry(actorId, "DEPARTMENT_STUDENTS_EXPORTED", "DEPARTMENT", deptId,
            new Dictionary<string, object?> { ["rowCount"] = filtered.Count, ["semesterId"] = query.SemesterId }), ct);

        return new RosterFile(bytes, $"department-students-{deptId}-{Now:yyyyMMdd}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}
