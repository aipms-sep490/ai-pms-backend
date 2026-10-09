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
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.Status == "ACTIVE"
            && u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), ct)) throw new ForbiddenException();
        if (query.SemesterId <= 0 || query.DepartmentId <= 0 || query.MajorId <= 0 || query.TeamId <= 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["filters"] = ["Positive semesterId and filter IDs required."] });
        if (!await db.AcademicSemesters.AnyAsync(s => s.Id == query.SemesterId, ct)) throw new NotFoundException("Semester", query.SemesterId);
        if (query.DepartmentId is long department && !await db.Departments.AnyAsync(d => d.Id == department, ct))
            throw new NotFoundException("Department", department);
        if (query.MajorId is long major && !await db.Majors.AnyAsync(m => m.Id == major
            && (query.DepartmentId == null || m.DepartmentId == query.DepartmentId), ct)) throw new NotFoundException("Major in department", major);
        if (query.TeamId is long team && !await db.Teams.AnyAsync(t => t.Id == team && t.AcademicSemesterId == query.SemesterId, ct))
            throw new NotFoundException("Team in semester", team);

        // Filters apply to each current member, not to every member of a matching team.
        var rows = await db.TeamMembers.AsNoTracking()
            .Where(m => m.LeftAt == null && m.Team.AcademicSemesterId == query.SemesterId
                && (query.TeamId == null || m.TeamId == query.TeamId)
                && (query.DepartmentId == null || m.User.DepartmentId == query.DepartmentId)
                && (query.MajorId == null || m.User.MajorId == query.MajorId))
            .OrderBy(m => m.Team.Code).ThenBy(m => m.TeamId).ThenByDescending(m => m.IsLeader)
            .ThenBy(m => m.User.StudentCode).ThenBy(m => m.UserId).ThenBy(m => m.Id)
            .Select(m => new TeamRosterRow(m.TeamId, m.Team.Code, m.UserId, m.IsLeader,
                m.User.StudentCode, m.User.FullName, m.User.Phone, m.User.Email, m.User.CurriculumCode))
            .Take(MaxRows + 1).ToListAsync(ct);
        if (rows.Count > MaxRows) throw new DomainException("ROSTER_EXPORT_ROW_LIMIT_EXCEEDED: narrow filters to at most 10000 students.");
        var bytes = TeamRosterWorkbook.Create(rows);
        await audit.RecordAsync(new(actor, "TEAM_ROSTER_EXPORTED", "ACADEMIC_SEMESTER", query.SemesterId,
            new Dictionary<string, object?> { ["format"] = "xlsx", ["filters"] = query, ["rowCount"] = rows.Count }), ct);
        await transaction.CommitAsync(ct);
        return new(bytes, $"team-roster-{query.SemesterId}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}
