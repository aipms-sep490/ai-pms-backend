using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Application.Features.StudentRosters.Abstractions;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.StudentRosters;

internal sealed class TeamRosterExportService(AipmsDbContext db, ICurrentUser currentUser, IAuditTrail audit)
    : ITeamRosterExportService
{
    internal const int MaxRows = 10_000;

    public async Task<RosterFile> ExportAsync(RosterExportQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actor) throw new UnauthorizedException();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var caller = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF")
                    && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
            })
            .SingleOrDefaultAsync(ct);
        if (caller is null || (!caller.IsAdmin && !caller.IsStaff)) throw new ForbiddenException();

        var effectiveDepartmentId = query.DepartmentId;
        if (!caller.IsAdmin)
        {
            if (!caller.DepartmentId.HasValue)
                throw new ForbiddenException("Department staff must be assigned to an active department.", WorkflowErrorCodes.ScopeAccessDenied);
            if (query.DepartmentId.HasValue && query.DepartmentId.Value != caller.DepartmentId.Value)
                throw new ForbiddenException("Department staff can only export team rosters within their own department.", WorkflowErrorCodes.ScopeAccessDenied);
            effectiveDepartmentId = caller.DepartmentId.Value;
        }

        if (query.SemesterId <= 0 || effectiveDepartmentId <= 0 || query.MajorId <= 0 || query.TeamId <= 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["filters"] = ["Positive semesterId and filter IDs required."] });
        if (!await db.AcademicSemesters.AnyAsync(s => s.Id == query.SemesterId, ct)) throw new NotFoundException("Semester", query.SemesterId);
        if (effectiveDepartmentId is long department && !await db.Departments.AnyAsync(d => d.Id == department, ct))
            throw new NotFoundException("Department", department);
        if (query.MajorId is long major && !await db.Majors.AnyAsync(m => m.Id == major
            && (effectiveDepartmentId == null || m.DepartmentId == effectiveDepartmentId), ct))
            throw caller.IsAdmin ? new NotFoundException("Major in department", major) : new ForbiddenException("Major is outside your department.", WorkflowErrorCodes.ScopeAccessDenied);
        if (query.TeamId is long team && !await db.Teams.AnyAsync(t => t.Id == team && t.AcademicSemesterId == query.SemesterId, ct))
            throw new NotFoundException("Team in semester", team);

        // Filters apply to each current member, not to every member of a matching team.
        var rows = await db.TeamMembers.AsNoTracking()
            .Where(m => m.LeftAt == null && m.Team.AcademicSemesterId == query.SemesterId
                && (query.TeamId == null || m.TeamId == query.TeamId)
                && (effectiveDepartmentId == null || m.User.DepartmentId == effectiveDepartmentId)
                && (query.MajorId == null || m.User.MajorId == query.MajorId))
            .OrderBy(m => m.Team.Code).ThenBy(m => m.TeamId).ThenByDescending(m => m.IsLeader)
            .ThenBy(m => m.User.StudentCode).ThenBy(m => m.UserId).ThenBy(m => m.Id)
            .Select(m => new TeamRosterRow(m.TeamId, m.Team.Code, m.UserId, m.IsLeader,
                m.User.StudentCode, m.User.FullName, m.User.Phone, m.User.Email, m.User.CurriculumCode,
                m.User.ClassCode, m.User.Specialization == null ? null : m.User.Specialization.Name))
            .Take(MaxRows + 1).ToListAsync(ct);
        if (rows.Count > MaxRows) throw new DomainException("ROSTER_EXPORT_ROW_LIMIT_EXCEEDED: narrow filters to at most 10000 students.");
        var bytes = TeamRosterWorkbook.Create(rows);
        await audit.RecordAsync(new(actor, "TEAM_ROSTER_EXPORTED", "ACADEMIC_SEMESTER", query.SemesterId,
            new Dictionary<string, object?> { ["format"] = "xlsx", ["filters"] = query with { DepartmentId = effectiveDepartmentId }, ["rowCount"] = rows.Count }), ct);
        await transaction.CommitAsync(ct);
        return new(bytes, $"team-roster-{query.SemesterId}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}
