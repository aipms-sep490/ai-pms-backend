using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Commands;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Services;

public sealed class TeamWorkflow(
    ITeamRepository repository, ITeamFormationPolicyProvider policies,
    ICurrentUser currentUser, IAuditTrail audit, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<TeamParticipant> ActorAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();
        if (!currentUser.Roles.Contains(AppRoles.Student))
            throw new ForbiddenException("Only students can use team membership operations.");
        return await repository.GetStudentAsync(currentUser.UserId.Value, ct)
            ?? throw new ForbiddenException("An active student account is required.");
    }

    private async Task<TeamSnapshot> TeamAsync(long id, CancellationToken ct) =>
        await repository.GetAsync(id, ct) ?? throw new NotFoundException("Team", id);

    private static void RequireMember(TeamSnapshot team, long actorId, bool leader = false)
    {
        if (!team.Members.Any(m => m.UserId == actorId && (!leader || m.IsLeader)))
            throw new ForbiddenException(leader
                ? "Only the current team leader can perform this action."
                : "Only active team members can view this team.");
    }

    private async Task<(TeamRegistrationWindow Window, TeamFormationPolicy Policy)> ContextAsync(
        long semesterId, CancellationToken ct)
    {
        var window = await repository.GetOpenWindowAsync(semesterId, Now, ct)
            ?? throw new ConflictException("No unambiguous active registration window exists in this semester.");
        var policy = await policies.GetAsync(window.PeriodId, ct);
        if (policy is null || !policy.IsValid)
            throw new ConflictException("Team formation policy is not configured for this registration period (BE-12).");
        return (window, policy);
    }

    private async Task<(TeamRegistrationWindow Window, TeamFormationPolicy Policy)> MutableAsync(
        TeamSnapshot team, CancellationToken ct)
    {
        if (team.Status is not ("FORMING" or "ELIGIBLE")
            || team.ProjectStatuses.Any(TeamRules.ProjectLocksRoster))
            throw new ConflictException("The team roster is locked by team or project status.");
        return await ContextAsync(team.SemesterId, ct);
    }

    private static void RequireEligibleStudent(TeamParticipant student, long organizationId)
    {
        if (!student.IsEligibleStudent || student.MajorId is null || student.OrganizationId != organizationId)
            throw new ConflictException("The student must have an active academic profile in the semester's organization.");
    }

    private async Task RequireNoTeamAsync(long semesterId, long userId, CancellationToken ct)
    {
        if (await repository.GetCurrentTeamIdAsync(semesterId, userId, ct) is not null)
            throw new ConflictException("The student already belongs to an active team in this semester.");
    }

    private async Task<TeamDto> MapAsync(TeamSnapshot team, CancellationToken ct)
    {
        var reasons = new List<string>();
        var window = await repository.GetOpenWindowAsync(team.SemesterId, Now, ct);
        var policy = window is null ? null : await policies.GetAsync(window.PeriodId, ct);
        if (window is null) reasons.Add("REGISTRATION_WINDOW_UNAVAILABLE");
        if (policy is null || !policy.IsValid) reasons.Add("TEAM_POLICY_UNCONFIGURED");
        if (team.Status is not ("FORMING" or "ELIGIBLE")
            || team.ProjectStatuses.Any(TeamRules.ProjectLocksRoster)) reasons.Add("ROSTER_LOCKED");
        var locked = reasons.Count > 0;
        if (window is not null && policy is { IsValid: true })
            reasons.AddRange(TeamRules.EligibilityErrors(team.Members, policy, window.OrganizationId));
        return new TeamDto(team.Id, team.SemesterId, team.Code, team.Name, team.Description,
            team.Status, team.Members.Select(member => member.ToDto()).ToArray(),
            new TeamEligibilityDto(reasons.Count == 0, locked,
                window?.PeriodId, policy?.Version, reasons));
    }

    private async Task<TeamDto> SaveEligibilityAsync(long teamId, CancellationToken ct)
    {
        var team = await TeamAsync(teamId, ct);
        var (window, policy) = await MutableAsync(team, ct);
        var status = TeamRules.EligibilityErrors(team.Members, policy, window.OrganizationId).Count == 0
            ? "ELIGIBLE" : "FORMING";
        await repository.UpdateAsync(team.Id, team.Name, team.Description, status, Now, ct);
        return await MapAsync(team with { Status = status }, ct);
    }

    private async Task AuditAsync(string action, long teamId, long actorId, long? subjectId,
        CancellationToken ct)
    {
        var team = await TeamAsync(teamId, ct);
        var window = await repository.GetOpenWindowAsync(team.SemesterId, Now, ct);
        var policy = window is null ? null : await policies.GetAsync(window.PeriodId, ct);
        await audit.RecordAsync(new AuditEntry(actorId, action, "TEAM", teamId,
            new Dictionary<string, object?>
            {
                ["subjectId"] = subjectId,
                ["academicSemesterId"] = team.SemesterId,
                ["registrationPeriodId"] = window?.PeriodId,
                ["policyVersion"] = policy?.Version,
                ["status"] = team.Status
            }), ct);
    }

    public async Task<TeamDto> GetAsync(long teamId, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var team = await TeamAsync(teamId, ct);
        RequireMember(team, actor.UserId);
        return await MapAsync(team, ct);
    }

    private static void RequireSameMajorAsLeader(TeamSnapshot team, TeamParticipant student, long organizationId)
    {
        var leaders = team.Members.Where(m => m.IsLeader).ToArray();
        if (leaders.Length != 1)
            throw new ConflictException("The team must have exactly one active leader.");
        var leader = leaders[0];
        RequireEligibleStudent(leader, organizationId);
        if (!TeamRules.HasSameMajor(student, leader)
            || team.Members.Any(m => !TeamRules.HasSameMajor(m, leader)))
            throw new ConflictException("All team members must belong to the same major as the team leader.");
    }

    public async Task ValidateRegistrationAsync(long teamId, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var team = await TeamAsync(teamId, ct);
        RequireMember(team, actor.UserId, true);
        var (window, policy) = await MutableAsync(team, ct);
        var reasons = TeamRules.EligibilityErrors(team.Members, policy, window.OrganizationId);
        if (reasons.Count != 0)
            throw new ConflictException("The team is no longer eligible to register: " + string.Join(", ", reasons));

        // The stored status is a cache, not the source of truth for registration.
        if (team.Status != "ELIGIBLE")
        {
            await repository.UpdateAsync(team.Id, team.Name, team.Description, "ELIGIBLE", Now, ct);
            await AuditAsync("TEAM_ELIGIBILITY_REFRESHED", team.Id, actor.UserId, null, ct);
        }
    }

    public async Task<TeamDto?> CurrentAsync(long semesterId, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var id = await repository.GetCurrentTeamIdAsync(semesterId, actor.UserId, ct);
        return id is null ? null : await MapAsync(await TeamAsync(id.Value, ct), ct);
    }

    public Task<TeamDto> CreateAsync(CreateTeamCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var (window, _) = await ContextAsync(request.AcademicSemesterId, token);
            RequireEligibleStudent(actor, window.OrganizationId);
            await RequireNoTeamAsync(request.AcademicSemesterId, actor.UserId, token);
            var now = Now;
            var id = await repository.CreateAsync(request.AcademicSemesterId,
                request.Code.Trim().ToUpperInvariant(), request.Name.Trim(),
                request.Description?.Trim(), actor.UserId, now, token);
            await repository.AddMemberAsync(id, request.AcademicSemesterId, actor.UserId, true, now, token);
            var result = await SaveEligibilityAsync(id, token);
            await AuditAsync("TEAM_CREATED", id, actor.UserId, actor.UserId, token);
            return result;
        }, ct);

    public Task<TeamDto> UpdateAsync(UpdateTeamCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var team = await TeamAsync(request.TeamId, token);
            RequireMember(team, actor.UserId, true);
            await MutableAsync(team, token);
            await repository.UpdateAsync(team.Id, request.Name.Trim(), request.Description?.Trim(), team.Status, Now, token);
            var result = await SaveEligibilityAsync(team.Id, token);
            await AuditAsync("TEAM_UPDATED", team.Id, actor.UserId, null, token);
            return result;
        }, ct);

    public Task<TeamDto> RefreshAsync(long teamId, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var team = await TeamAsync(teamId, token);
            RequireMember(team, actor.UserId, true);
            var result = await SaveEligibilityAsync(teamId, token);
            await AuditAsync("TEAM_ELIGIBILITY_REFRESHED", teamId, actor.UserId, null, token);
            return result;
        }, ct);

    public async Task<TeamInvitationCandidateScope> GetInvitationCandidateScopeAsync(long teamId, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var team = await TeamAsync(teamId, ct);
        RequireMember(team, actor.UserId, true);
        var (window, policy) = await MutableAsync(team, ct);
        RequireEligibleStudent(actor, window.OrganizationId);
        RequireSameMajorAsLeader(team, actor, window.OrganizationId);
        if (team.Members.Count >= policy.MaxMembers)
            throw new ConflictException("The team has reached its member limit.");
        return new(team.Id, team.SemesterId, actor.MajorId!.Value, window.OrganizationId, Now);
    }

    public Task<TeamInvitationDto> InviteAsync(InviteTeamMemberCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var team = await TeamAsync(request.TeamId, token);
            RequireMember(team, actor.UserId, true);
            var (window, policy) = await MutableAsync(team, token);
            var invited = await repository.GetStudentAsync(request.InvitedUserId, token)
                ?? throw new NotFoundException("Student", request.InvitedUserId);
            RequireEligibleStudent(invited, window.OrganizationId);
            RequireSameMajorAsLeader(team, invited, window.OrganizationId);
            await RequireNoTeamAsync(team.SemesterId, invited.UserId, token);
            if (team.Members.Count >= policy.MaxMembers)
                throw new ConflictException("The team has reached its member limit.");
            var pending = await repository.GetPendingInvitationAsync(team.Id, invited.UserId, token);
            var now = Now;
            if (pending is not null)
            {
                if (!TeamRules.IsInvitationExpired(pending.ExpiresAt, now))
                    throw new ConflictException("A pending invitation already exists.");
                await repository.RespondAsync(pending.Id, "EXPIRED", now, token);
            }
            var expiry = now.AddHours(policy.InvitationHours);
            if (expiry > window.EndAt) expiry = window.EndAt;
            var result = await repository.InviteAsync(team.Id, invited.UserId, actor.UserId,
                request.Message?.Trim(), expiry, now, token);
            await AuditAsync("TEAM_INVITED", team.Id, actor.UserId, invited.UserId, token);
            return result.ToDto();
        }, ct);

    public Task<TeamDto> AcceptAsync(long invitationId, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var invitation = await OwnInvitationAsync(invitationId, actor.UserId, token);
            var team = await TeamAsync(invitation.TeamId, token);
            var (window, policy) = await MutableAsync(team, token);
            RequireEligibleStudent(actor, window.OrganizationId);
            RequireSameMajorAsLeader(team, actor, window.OrganizationId);
            await RequireNoTeamAsync(team.SemesterId, actor.UserId, token);
            if (team.Members.Count >= policy.MaxMembers)
                throw new ConflictException("The team has reached its member limit.");
            var now = Now;
            await repository.AddMemberAsync(team.Id, team.SemesterId, actor.UserId, false, now, token);
            await repository.RespondAsync(invitation.Id, "ACCEPTED", now, token);
            var result = await SaveEligibilityAsync(team.Id, token);
            await AuditAsync("TEAM_INVITATION_ACCEPTED", team.Id, actor.UserId, invitation.Id, token);
            return result;
        }, ct);

    private async Task<TeamInvitationData> OwnInvitationAsync(long id, long actorId, CancellationToken ct)
    {
        var invitation = await repository.GetInvitationAsync(id, ct)
            ?? throw new NotFoundException("Team invitation", id);
        if (invitation.InvitedUserId != actorId)
            throw new ForbiddenException("Only the invitation recipient can respond.");
        if (invitation.Status != "PENDING" || TeamRules.IsInvitationExpired(invitation.ExpiresAt, Now))
            throw new ConflictException("The invitation is expired or has already been processed.");
        return invitation;
    }

    public Task<bool> RejectAsync(long invitationId, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var invitation = await OwnInvitationAsync(invitationId, actor.UserId, token);
            await repository.RespondAsync(invitation.Id, "REJECTED", Now, token);
            await AuditAsync("TEAM_INVITATION_REJECTED", invitation.TeamId, actor.UserId, invitation.Id, token);
            return true;
        }, ct);

    public Task<bool> CancelAsync(long invitationId, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var invitation = await repository.GetInvitationAsync(invitationId, token)
                ?? throw new NotFoundException("Team invitation", invitationId);
            RequireMember(await TeamAsync(invitation.TeamId, token), actor.UserId, true);
            if (invitation.Status != "PENDING" || TeamRules.IsInvitationExpired(invitation.ExpiresAt, Now))
                throw new ConflictException("The invitation is expired or has already been processed.");
            await repository.RespondAsync(invitationId, "CANCELLED", Now, token);
            await AuditAsync("TEAM_INVITATION_CANCELLED", invitation.TeamId, actor.UserId, invitationId, token);
            return true;
        }, ct);

    public Task<bool> RemoveAsync(long teamId, long? memberId, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var team = await TeamAsync(teamId, token);
            var targetId = memberId ?? actor.UserId;
            RequireMember(team, actor.UserId, targetId != actor.UserId);
            await MutableAsync(team, token);
            var member = team.Members.SingleOrDefault(m => m.UserId == targetId)
                ?? throw new NotFoundException("Team member", targetId);
            if (member.IsLeader)
                throw new ConflictException("The leader must transfer leadership before leaving.");
            await repository.RemoveMemberAsync(teamId, targetId, Now, token);
            await SaveEligibilityAsync(teamId, token);
            await AuditAsync(targetId == actor.UserId ? "TEAM_LEFT" : "TEAM_MEMBER_REMOVED",
                teamId, actor.UserId, targetId, token);
            return true;
        }, ct);

    public Task<TeamDto> TransferAsync(TransferTeamLeaderCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var team = await TeamAsync(request.TeamId, token);
            RequireMember(team, actor.UserId, true);
            var (window, _) = await MutableAsync(team, token);
            var member = team.Members.SingleOrDefault(m => m.UserId == request.NewLeaderUserId)
                ?? throw new ConflictException("The new leader must be an active member of this team.");
            RequireEligibleStudent(member, window.OrganizationId);
            RequireSameMajorAsLeader(team, member, window.OrganizationId);
            if (member.UserId == actor.UserId)
                throw new ConflictException("This student is already the leader.");
            await repository.TransferLeaderAsync(team.Id, actor.UserId, member.UserId, Now, token);
            var result = await SaveEligibilityAsync(team.Id, token);
            await AuditAsync("TEAM_LEADER_TRANSFERRED", team.Id, actor.UserId, member.UserId, token);
            return result;
        }, ct);

    public async Task<PagedResult<TeamInvitationDto>> InvitationsAsync(
        long? teamId, int page, int pageSize, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        if (teamId is not null) RequireMember(await TeamAsync(teamId.Value, ct), actor.UserId, true);
        var result = await repository.GetInvitationsAsync(actor.UserId, teamId, page, pageSize, ct);
        var now = Now;
        var items = result.Items.Select(i =>
            (i.Status == "PENDING" && TeamRules.IsInvitationExpired(i.ExpiresAt, now)
                ? i with { Status = "EXPIRED" } : i).ToDto()).ToArray();
        return new PagedResult<TeamInvitationDto>(items, result.Page, result.PageSize, result.TotalCount);
    }
}
