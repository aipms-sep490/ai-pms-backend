using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using TeamLeaderChangeRequestEntity = AIPMS.Infrastructure.Persistence.Generated.Models.TeamLeaderChangeRequest;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class TeamLeaderChangeRequestRepository(AipmsDbContext context)
    : ITeamLeaderChangeRequestRepository
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
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            if (ex is SqlException { Number: 1205 or 2601 or 2627 })
                throw new ConflictException("The leader-change request changed concurrently. Reload and retry.");
            throw;
        }
    }

    public async Task LockTeamAsync(long teamId, CancellationToken ct) =>
        await context.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.teams WITH (UPDLOCK, HOLDLOCK) WHERE id = {teamId}")
            .ToListAsync(ct);

    public async Task LockRequestAsync(long requestId, CancellationToken ct) =>
        await context.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.team_leader_change_requests WITH (UPDLOCK, HOLDLOCK) WHERE id = {requestId}")
            .ToListAsync(ct);

    public async Task<TeamLeaderChangeContext?> GetContextAsync(long teamId, long newLeaderUserId, CancellationToken ct)
    {
        var team = await context.Teams.Where(t => t.Id == teamId)
            .Select(t => new
            {
                t.Id,
                ProjectId = t.Project == null ? (long?)null : t.Project.Id,
                CurrentLeaderUserId = t.TeamMembers.Where(m => m.LeftAt == null && m.IsLeader)
                    .Select(m => (long?)m.UserId).SingleOrDefault()
            }).SingleOrDefaultAsync(ct);
        if (team is null) return null;
        if (!await context.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == newLeaderUserId && m.LeftAt == null, ct))
            return new(team.Id, team.ProjectId, team.CurrentLeaderUserId ?? 0, newLeaderUserId, null, null, null);
        if (team.ProjectId is not long projectId)
            return new(team.Id, null, team.CurrentLeaderUserId ?? 0, newLeaderUserId, null, null, null);
        var mentor = await context.SupervisorAssignments.Where(a => a.ProjectId == projectId
                && a.IsPrimary && a.EndedAt == null
                && a.SupervisorProfile.User.Status == "ACTIVE"
                && a.SupervisorProfile.User.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
                && a.SupervisorProfile.User.Department != null
                && a.SupervisorProfile.User.Department.IsActive
                && a.SupervisorProfile.User.Department.Organization.IsActive
                && a.SupervisorProfile.User.Department.OrganizationId == a.Project.Team.AcademicSemester.OrganizationId)
            .Select(a => new { a.SupervisorProfileId, a.SupervisorProfile.UserId, a.SupervisorProfile.User.FullName })
            .SingleOrDefaultAsync(ct);
        return new(team.Id, projectId, team.CurrentLeaderUserId ?? 0, newLeaderUserId,
            mentor?.SupervisorProfileId, mentor?.UserId, mentor?.FullName);
    }

    public Task<bool> HasPendingAsync(long teamId, CancellationToken ct) =>
        context.Database.SqlQuery<bool>($"SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.team_leader_change_requests WHERE team_id = {teamId} AND status = N'PENDING') THEN 1 ELSE 0 END AS bit) AS Value")
            .SingleAsync(ct);

    public async Task<TeamLeaderChangeRequestModel> CreateAsync(long teamId, long projectId,
        long requestedBy, long currentLeaderUserId, long newLeaderUserId, long mentorProfileId,
        string? message, DateTime now, CancellationToken ct)
    {
        var entity = new TeamLeaderChangeRequestEntity
        {
            TeamId = teamId,
            ProjectId = projectId,
            RequestedBy = requestedBy,
            CurrentLeaderUserId = currentLeaderUserId,
            NewLeaderUserId = newLeaderUserId,
            MentorProfileId = mentorProfileId,
            Status = "PENDING",
            RequestMessage = message,
            RequestedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        context.TeamLeaderChangeRequests.Add(entity);
        await context.SaveChangesAsync(ct);
        return (await GetAsync(entity.Id, ct))!;
    }

    public Task<TeamLeaderChangeRequestModel?> GetAsync(long requestId, CancellationToken ct) =>
        ProjectQuery(context.TeamLeaderChangeRequests.AsNoTracking()
            .Where(r => r.Id == requestId)).SingleOrDefaultAsync(ct);

    public async Task<TeamLeaderChangeRequestModel> RespondAsync(long requestId, string status,
        string? message, DateTime now, CancellationToken ct)
    {
        var affected = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.team_leader_change_requests
            SET status = {status}, response_message = {message}, responded_at = {now}, updated_at = {now}
            WHERE id = {requestId} AND status = N'PENDING'
            """, ct);
        if (affected != 1) throw new ConflictException("The leader-change request has already been processed.");
        return (await GetAsync(requestId, ct))!;
    }

    public async Task<PagedResult<TeamLeaderChangeRequestModel>> SearchAsync(
        TeamLeaderChangeRequestSearch search, CancellationToken ct)
    {
        var query = context.TeamLeaderChangeRequests.AsNoTracking();
        if (search.TeamId.HasValue) query = query.Where(x => x.TeamId == search.TeamId.Value);
        if (search.MentorUserId.HasValue)
            query = query.Where(x => x.MentorProfile.UserId == search.MentorUserId.Value);
        if (search.Status is not null) query = query.Where(x => x.Status == search.Status);
        var count = await query.LongCountAsync(ct);
        var page = query.OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize);
        var items = await ProjectQuery(page).ToListAsync(ct);
        return new(items, search.Page, search.PageSize, count);
    }

    public Task<bool> HasActiveMentorAsync(long projectId, long mentorProfileId, CancellationToken ct) =>
        context.Database.SqlQuery<bool>($"SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.supervisor_assignments WHERE project_id = {projectId} AND supervisor_profile_id = {mentorProfileId} AND is_primary = 1 AND ended_at IS NULL) THEN 1 ELSE 0 END AS bit) AS Value")
            .SingleAsync(ct);

    private static IQueryable<TeamLeaderChangeRequestModel> ProjectQuery(
        IQueryable<TeamLeaderChangeRequestEntity> query) => query.Select(r => new TeamLeaderChangeRequestModel(
            r.Id, r.TeamId, r.ProjectId,
            r.RequestedBy, r.CurrentLeaderUserId, r.NewLeaderUserId, r.MentorProfileId,
            r.MentorProfile.UserId, r.MentorProfile.User.FullName, r.Status, r.RequestMessage,
            r.ResponseMessage, r.RequestedAt, r.RespondedAt));
}
