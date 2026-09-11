using System.Data;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Team = AIPMS.Infrastructure.Persistence.Generated.Models.Team;
using TeamInvitation = AIPMS.Infrastructure.Persistence.Generated.Models.TeamInvitation;
using TeamMember = AIPMS.Infrastructure.Persistence.Generated.Models.TeamMember;
using User = AIPMS.Infrastructure.Persistence.Generated.Models.User;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class TeamRepository(AipmsDbContext context) : ITeamRepository
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            if (IsWriteConflict(exception))
                throw new ConflictException("Team membership changed concurrently or a team code/invitation already exists. Refresh and retry.");
            throw;
        }
    }

    private static bool IsWriteConflict(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 or 2601 or 2627 }) return true;
        return false;
    }

    // Every roster mutation acquires the same team update/range lock before checking capacity.
    // Serializable membership reads + unique filtered indexes arbitrate cross-team races.
    public async Task<TeamSnapshot?> GetAsync(long teamId, CancellationToken ct)
    {
        var query = context.Database.CurrentTransaction is null
            ? context.Teams.Where(t => t.Id == teamId)
            : context.Teams.FromSqlInterpolated(
                $"SELECT * FROM dbo.teams WITH (UPDLOCK, HOLDLOCK) WHERE id = {teamId}");
        var team = await query.SingleOrDefaultAsync(ct);
        if (team is null) return null;
        var memberships = await context.TeamMembers.Where(m => m.TeamId == teamId && m.LeftAt == null)
            .Select(m => new { m.UserId, m.IsLeader }).ToListAsync(ct);
        var ids = memberships.Select(m => m.UserId).ToArray();
        var students = await context.Users.Where(u => ids.Contains(u.Id)).Select(StudentProjection).ToListAsync(ct);
        var members = students.Select(s => s with { IsLeader = memberships.Single(m => m.UserId == s.UserId).IsLeader }).ToArray();
        var statuses = await context.Projects.Where(p => p.TeamId == teamId).Select(p => p.Status).ToListAsync(ct);
        return new TeamSnapshot(team.Id, team.AcademicSemesterId, team.Code, team.Name,
            team.Description, team.Status, members, statuses);
    }

    private static readonly Expression<Func<User, TeamParticipant>> StudentProjection = u =>
        new TeamParticipant(u.Id, u.FullName, u.MajorId,
            u.Major != null ? u.Major.Department.OrganizationId : null,
            u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")
                && u.Major != null && u.Major.IsActive && u.Major.Department.IsActive
                && u.Major.Department.Organization.IsActive
                && u.DepartmentId == u.Major.DepartmentId, false);

    public Task<TeamParticipant?> GetStudentAsync(long userId, CancellationToken ct) =>
        context.Users.Where(u => u.Id == userId && u.Status == "ACTIVE"
            && u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"))
            .Select(StudentProjection).SingleOrDefaultAsync(ct);

    public Task<long?> GetCurrentTeamIdAsync(long semesterId, long userId, CancellationToken ct) =>
        context.TeamMembers.Where(m => m.AcademicSemesterId == semesterId && m.UserId == userId && m.LeftAt == null)
            .Select(m => (long?)m.TeamId).SingleOrDefaultAsync(ct);

    public async Task<TeamRegistrationWindow?> GetOpenWindowAsync(long semesterId, DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var periods = await context.ProjectPeriods
            .Where(p => p.AcademicSemesterId == semesterId && p.PeriodType == "REGISTRATION"
                && p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt
                && p.AcademicSemester.Status == "ACTIVE"
                && p.AcademicSemester.StartDate <= today && today <= p.AcademicSemester.EndDate
                && p.AcademicSemester.Organization.IsActive)
            .OrderBy(p => p.Id).Select(p => new TeamRegistrationWindow(
                p.Id, p.AcademicSemesterId, p.AcademicSemester.OrganizationId, p.EndAt))
            .Take(2).ToListAsync(ct);
        return periods.Count == 1 ? periods[0] : null;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Team writes require an explicit transaction.");
        await context.SaveChangesAsync(ct);
    }

    public async Task<long> CreateAsync(long semesterId, string code, string name, string? description,
        long actorId, DateTime now, CancellationToken ct)
    {
        var team = new Team
        {
            AcademicSemesterId = semesterId, Code = code, Name = name, Description = description,
            Status = "FORMING", CreatedBy = actorId, CreatedAt = now, UpdatedAt = now
        };
        context.Teams.Add(team);
        await SaveAsync(ct);
        return team.Id;
    }

    public async Task UpdateAsync(long teamId, string name, string? description, string status, DateTime now, CancellationToken ct)
    {
        var team = await context.Teams.SingleAsync(t => t.Id == teamId, ct);
        team.Name = name;
        team.Description = description;
        team.Status = status;
        team.UpdatedAt = now;
        await SaveAsync(ct);
    }

    public async Task AddMemberAsync(long teamId, long semesterId, long userId, bool isLeader, DateTime now, CancellationToken ct)
    {
        // (team_id, user_id) is unique even after leaving; reactivate, never insert a duplicate.
        var member = await context.TeamMembers.SingleOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, ct);
        if (member is null)
        {
            member = new TeamMember { TeamId = teamId, AcademicSemesterId = semesterId, UserId = userId, CreatedAt = now };
            context.TeamMembers.Add(member);
        }
        else if (member.LeftAt is null)
            throw new ConflictException("The student is already an active member.");
        member.IsLeader = isLeader;
        member.JoinedAt = now;
        member.LeftAt = null;
        member.UpdatedAt = now;
        await SaveAsync(ct);
    }

    public async Task RemoveMemberAsync(long teamId, long userId, DateTime now, CancellationToken ct)
    {
        var member = await context.TeamMembers.SingleAsync(m => m.TeamId == teamId && m.UserId == userId && m.LeftAt == null, ct);
        member.LeftAt = now;
        member.IsLeader = false;
        member.UpdatedAt = now;
        await SaveAsync(ct);
    }

    public async Task TransferLeaderAsync(long teamId, long oldLeaderId, long newLeaderId, DateTime now, CancellationToken ct)
    {
        var oldLeader = await context.TeamMembers.SingleAsync(
            m => m.TeamId == teamId && m.UserId == oldLeaderId && m.LeftAt == null && m.IsLeader, ct);
        oldLeader.IsLeader = false;
        oldLeader.UpdatedAt = now;
        // Two saves inside ONE transaction respect the unique active-leader index.
        await SaveAsync(ct);
        var newLeader = await context.TeamMembers.SingleAsync(m => m.TeamId == teamId && m.UserId == newLeaderId && m.LeftAt == null, ct);
        newLeader.IsLeader = true;
        newLeader.UpdatedAt = now;
        await SaveAsync(ct);
    }

    private static readonly Expression<Func<TeamInvitation, TeamInvitationData>> InvitationProjection = i =>
        new TeamInvitationData(i.Id, i.TeamId, i.InvitedUserId, i.InvitedBy,
            i.Status, i.Message, i.ExpiresAt, i.RespondedAt, i.CreatedAt);

    public Task<TeamInvitationData?> GetInvitationAsync(long invitationId, CancellationToken ct) =>
        context.TeamInvitations.Where(i => i.Id == invitationId).Select(InvitationProjection).SingleOrDefaultAsync(ct);

    public Task<TeamInvitationData?> GetPendingInvitationAsync(long teamId, long userId, CancellationToken ct) =>
        context.TeamInvitations.Where(i => i.TeamId == teamId && i.InvitedUserId == userId && i.Status == "PENDING")
            .Select(InvitationProjection).SingleOrDefaultAsync(ct);

    public async Task<TeamInvitationData> InviteAsync(long teamId, long invitedUserId, long actorId,
        string? message, DateTime expiresAt, DateTime now, CancellationToken ct)
    {
        var invitation = new TeamInvitation
        {
            TeamId = teamId, InvitedUserId = invitedUserId, InvitedBy = actorId, Message = message,
            Status = "PENDING", ExpiresAt = expiresAt, CreatedAt = now, UpdatedAt = now
        };
        context.TeamInvitations.Add(invitation);
        await SaveAsync(ct);
        return (await GetInvitationAsync(invitation.Id, ct))!;
    }

    public async Task RespondAsync(long invitationId, string status, DateTime now, CancellationToken ct)
    {
        var invitation = await context.TeamInvitations.SingleAsync(i => i.Id == invitationId, ct);
        invitation.Status = status;
        invitation.RespondedAt = now;
        invitation.UpdatedAt = now;
        await SaveAsync(ct);
    }

    public async Task<PagedResult<TeamInvitationData>> GetInvitationsAsync(long userId, long? teamId,
        int page, int pageSize, CancellationToken ct)
    {
        var query = teamId.HasValue
            ? context.TeamInvitations.Where(i => i.TeamId == teamId)
            : context.TeamInvitations.Where(i => i.InvitedUserId == userId);
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(InvitationProjection).ToListAsync(ct);
        return new PagedResult<TeamInvitationData>(items, page, pageSize, count);
    }
}
