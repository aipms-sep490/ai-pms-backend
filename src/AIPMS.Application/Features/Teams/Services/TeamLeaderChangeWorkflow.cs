using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Commands;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Supervisors.Services;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Domain.Teams;
using MediatR;

namespace AIPMS.Application.Features.Teams.Services;

public sealed class TeamLeaderChangeWorkflow(
    ITeamRepository teams, ITeamLeaderChangeRequestRepository requests,
    ICurrentUser currentUser,
    IAuditTrail audit, TimeProvider clock, SupervisorAccessService supervisorAccess,
    IPublisher events, TeamWorkflow teamWorkflow)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<object> RequestOrTransferAsync(RequestOrTransferTeamLeaderCommand request, CancellationToken ct) =>
        requests.InTransactionAsync<object>(async token =>
        {
            var actor = await RequireStudentAsync(token);
            var team = await GetTeamAsync(request.TeamId, token);
            RequireMember(team, actor.UserId, true);
            RequireLeaderChangeState(team);
            var target = team.Members.SingleOrDefault(m => m.UserId == request.NewLeaderUserId)
                ?? throw new ConflictException("The new leader must be an active member of this team.");
            if (target.UserId == actor.UserId)
                throw new ConflictException("This student is already the leader.");
            var context = await requests.GetContextAsync(team.Id, target.UserId, token)
                ?? throw new NotFoundException("Team", team.Id);
            if (context.ProjectId is not null)
                return await RequestCoreAsync(request, actor.UserId, team, target, context, token);

            return await teamWorkflow.TransferWithinTransactionAsync(
                new TransferTeamLeaderCommand(team.Id, target.UserId), token);
        }, ct);

    public Task<TeamLeaderChangeRequestDto> RequestAsync(RequestTeamLeaderChangeCommand request, CancellationToken ct) =>
        requests.InTransactionAsync(async token =>
        {
            var actor = await RequireStudentAsync(token);
            var team = await GetTeamAsync(request.TeamId, token);
            RequireMember(team, actor.UserId, true);
            RequireLeaderChangeState(team);
            var target = team.Members.SingleOrDefault(m => m.UserId == request.NewLeaderUserId)
                ?? throw new ConflictException("The new leader must be an active member of this team.");
            RequireReplacementAllowed(team, target);
            if (target.UserId == actor.UserId)
                throw new ConflictException("This student is already the leader.");
            var context = await requests.GetContextAsync(team.Id, target.UserId, token)
                ?? throw new NotFoundException("Team", team.Id);
            if (context.ProjectId is not long projectId || context.MentorProfileId is not long mentorProfileId)
                throw new ConflictException("An active project mentor must be assigned before changing the team leader.");
            if (context.CurrentLeaderUserId != actor.UserId || context.MentorUserId is null)
                throw new ConflictException("The team leadership or mentor assignment changed. Reload and retry.");
            if (await requests.HasPendingAsync(team.Id, token))
                throw new ConflictException("A leader-change request is already pending for this team.");
            var result = await requests.CreateAsync(team.Id, projectId, actor.UserId,
                context.CurrentLeaderUserId, target.UserId, mentorProfileId,
                TrimMessage(request.Message), Now, token);
            await events.Publish(new WorkflowNotificationEvent(
                WorkflowNotificationKind.TeamLeaderChangeRequested, result.Id, actor.UserId, result.RequestedAt), token);
            await AuditAsync("TEAM_LEADER_CHANGE_REQUESTED", team, actor.UserId, target.UserId,
                result, token);
            return result.ToDto();
        }, ct);

    private async Task<object> RequestCoreAsync(RequestOrTransferTeamLeaderCommand request,
        long actorId, TeamSnapshot team, TeamParticipant target, TeamLeaderChangeContext context,
        CancellationToken token)
    {
        if (context.ProjectId is not long projectId || context.MentorProfileId is not long mentorProfileId
            || context.MentorUserId is null)
            throw new ConflictException("An active project mentor must be assigned before changing the team leader.");
        RequireReplacementAllowed(team, target);
        if (context.CurrentLeaderUserId != actorId)
            throw new ConflictException("The team leadership changed. Reload and retry.");
        if (await requests.HasPendingAsync(team.Id, token))
            throw new ConflictException("A leader-change request is already pending for this team.");
        var result = await requests.CreateAsync(team.Id, projectId, actorId, context.CurrentLeaderUserId,
            target.UserId, mentorProfileId, TrimMessage(request.Message), Now, token);
        await events.Publish(new WorkflowNotificationEvent(
            WorkflowNotificationKind.TeamLeaderChangeRequested, result.Id, actorId, result.RequestedAt), token);
        await AuditAsync("TEAM_LEADER_CHANGE_REQUESTED", team, actorId, target.UserId, result, token);
        return result.ToDto();
    }

    public Task<TeamLeaderChangeRequestDto> RespondAsync(long requestId, bool accept,
        string? message, CancellationToken ct) => requests.InTransactionAsync(async token =>
        {
            var actor = await RequireAuthenticatedAsync(token);
            await requests.LockRequestAsync(requestId, token);
            var request = await GetRequestAsync(requestId, token);
            var mentor = await supervisorAccess.EnsureCanReadAsync(token);
            if (actor.UserId != request.MentorUserId || mentor.UserId != request.MentorUserId
                || !mentor.HasActiveAcademicScope || !mentor.Roles.Contains(AppRoles.Lecturer))
                throw new ForbiddenException("Only the current project mentor can respond to this request.");
            var desired = accept ? "APPROVED" : "REJECTED";
            if (request.Status == desired) return request.ToDto();
            if (request.Status != "PENDING")
                throw new ConflictException("The leader-change request has already been processed.");

            var team = await GetTeamAsync(request.TeamId, token);
            if (!await requests.HasActiveMentorAsync(request.ProjectId, request.MentorProfileId, token))
                throw new ConflictException("The mentor assignment is no longer active.");
            if (accept)
            {
                RequireLeaderChangeState(team);
                var member = team.Members.SingleOrDefault(m => m.UserId == request.NewLeaderUserId)
                    ?? throw new ConflictException("The requested new leader is no longer an active team member.");
                RequireReplacementAllowed(team, member);
                if (team.Members.SingleOrDefault(m => m.IsLeader)?.UserId != request.CurrentLeaderUserId)
                    throw new ConflictException("The current team leader changed while this request was pending.");
                var context = await requests.GetContextAsync(team.Id, request.NewLeaderUserId, token);
                if (context?.ProjectId != request.ProjectId || context.MentorProfileId != request.MentorProfileId
                    || context.MentorUserId != request.MentorUserId
                    || !await requests.HasActiveMentorAsync(request.ProjectId, request.MentorProfileId, token))
                    throw new ConflictException("The mentor assignment is no longer active.");
                await teams.TransferLeaderAsync(team.Id, request.CurrentLeaderUserId,
                    request.NewLeaderUserId, Now, token);
            }
            var result = await requests.RespondAsync(request.Id, desired, TrimMessage(message), Now, token);
            await events.Publish(new WorkflowNotificationEvent(
                accept ? WorkflowNotificationKind.TeamLeaderChangeApproved : WorkflowNotificationKind.TeamLeaderChangeRejected,
                result.Id, actor.UserId, result.RespondedAt ?? Now), token);
            await AuditAsync(accept ? "TEAM_LEADER_CHANGE_APPROVED" : "TEAM_LEADER_CHANGE_REJECTED",
                team, actor.UserId, request.NewLeaderUserId, result, token);
            if (accept)
                await AuditAsync("TEAM_LEADER_TRANSFERRED", team, actor.UserId,
                    request.NewLeaderUserId, result, token);
            return result.ToDto();
        }, ct);

    public async Task<PagedResult<TeamLeaderChangeRequestDto>> ListAsync(long? teamId,
        string? status, int page, int pageSize, CancellationToken ct)
    {
        var actor = teamId.HasValue
            ? await RequireStudentAsync(ct)
            : await RequireAuthenticatedAsync(ct);
        long? mentorUserId = null;
        if (teamId.HasValue)
        {
            var team = await GetTeamAsync(teamId.Value, ct);
            if (!team.Members.Any(m => m.UserId == actor.UserId))
                throw new ForbiddenException("Only active team members can view leader-change requests.");
        }
        else
        {
            var mentor = await supervisorAccess.EnsureCanReadAsync(ct);
            if (!mentor.Roles.Contains(AppRoles.Lecturer))
                throw new ForbiddenException("Only lecturers can view the mentor leader-change inbox.");
            mentorUserId = mentor.UserId;
        }
        var result = await requests.SearchAsync(new(teamId, mentorUserId, status, page, pageSize), ct);
        return new(result.Items.Select(x => x.ToDto()).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    private async Task<TeamParticipant> RequireStudentAsync(CancellationToken ct)
    {
        var actor = await RequireAuthenticatedAsync(ct);
        if (!currentUser.Roles.Contains(AppRoles.Student))
            throw new ForbiddenException("Only students can request a leader change.");
        return await teams.GetStudentAsync(actor.UserId, ct)
            ?? throw new ForbiddenException("An active student account is required.");
    }

    private Task<TeamParticipant> RequireAuthenticatedAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long userId)
            throw new UnauthorizedException();
        return Task.FromResult(new TeamParticipant(userId, currentUser.FullName ?? string.Empty, null, null, true, false));
    }

    private async Task<TeamSnapshot> GetTeamAsync(long teamId, CancellationToken ct) =>
        await teams.GetAsync(teamId, ct) ?? throw new NotFoundException("Team", teamId);

    private static void RequireLeaderChangeState(TeamSnapshot team)
    {
        if (team.Status == "DISBANDED")
            throw new ConflictException("A disbanded team cannot change its leader.");
    }

    private static void RequireMember(TeamSnapshot team, long actorId, bool leader)
    {
        if (!team.Members.Any(m => m.UserId == actorId && (!leader || m.IsLeader)))
            throw new ForbiddenException("Only the current team leader can request a leader change.");
    }

    private static void RequireReplacementAllowed(TeamSnapshot team, TeamParticipant target)
    {
        if (!target.IsEligibleStudent || target.MajorId is null)
            throw new ConflictException("The new leader must have an active academic student profile.");
        if (team.AcademicScope is null)
        {
            var leader = team.Members.SingleOrDefault(m => m.IsLeader)
                ?? throw new ConflictException("The team must have exactly one active leader.");
            if (!TeamRules.HasSameMajor(target, leader)
                || team.Members.Any(m => !TeamRules.HasSameMajor(m, leader)))
                throw new ConflictException("All team members must belong to the same major as the team leader.");
            return;
        }
        if (team.AcademicScope.ProjectMode == "SINGLE_MAJOR"
            && target.MajorId != team.AcademicScope.PrimaryMajorId)
            throw new ConflictException("The new leader must belong to the team's primary major.");
        if (team.AcademicScope.ProjectMode == "INTERDISCIPLINARY"
            && !team.AcademicScope.Requirements.Any(r => r.MajorId == target.MajorId))
            throw new ConflictException("The new leader's major is outside the team's academic scope.");
    }

    private async Task<TeamLeaderChangeRequestModel> GetRequestAsync(long id, CancellationToken ct) =>
        await requests.GetAsync(id, ct) ?? throw new NotFoundException("TeamLeaderChangeRequest", id);

    private Task AuditAsync(string action, TeamSnapshot team, long actorId, long subjectId,
        TeamLeaderChangeRequestModel request, CancellationToken ct) =>
        audit.RecordAsync(new AuditEntry(actorId, action, "TEAM_LEADER_CHANGE_REQUEST", request.Id,
            new Dictionary<string, object?>
            {
                ["teamId"] = team.Id, ["projectId"] = request.ProjectId,
                ["currentLeaderUserId"] = request.CurrentLeaderUserId,
                ["newLeaderUserId"] = subjectId, ["status"] = request.Status
            }), ct);

    private static string? TrimMessage(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
