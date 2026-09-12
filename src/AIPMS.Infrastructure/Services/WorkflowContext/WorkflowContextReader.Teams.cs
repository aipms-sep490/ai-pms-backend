using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.WorkflowContext.DTOs;
using AIPMS.Domain.Teams;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Services.WorkflowContext;

internal sealed partial class WorkflowContextReader
{
    private sealed record TeamState(TeamSnapshot Team, TeamFormationPolicy? Policy,
        IReadOnlyList<string> MutableIssues, IReadOnlyList<string> EligibilityIssues, IReadOnlyList<string> InvitationIssues,
        bool WindowOpen, bool RosterUnlocked);

    private async Task<TeamState> ReadTeamStateAsync(long teamId, DateTime now, CancellationToken ct)
    {
        var team = await teams.GetAsync(teamId, ct) ?? throw new NotFoundException("Team", teamId);
        var window = await teams.GetOpenWindowAsync(team.SemesterId, now, ct);
        var policy = window is null ? null : await policies.GetAsync(window.PeriodId, ct);
        var unlocked = (team.Status is "FORMING" or "ELIGIBLE") && !team.ProjectStatuses.Any(TeamRules.ProjectLocksRoster);
        var mutable = new List<string>();
        if (!unlocked) mutable.Add("ROSTER_LOCKED");
        if (window is null) mutable.Add("REGISTRATION_WINDOW_UNAVAILABLE");
        if (policy is not { IsValid: true }) mutable.Add("TEAM_POLICY_UNCONFIGURED");
        if (team.AcademicScope is null && policy?.MinDistinctMajors > 1) mutable.Add("UNSUPPORTED_HYBRID_POLICY");
        if (team.AcademicScope is not null && window is not null)
        {
            try { await teams.ValidateAcademicScopeAsync(team.AcademicScope, window.OrganizationId, ct); }
            catch (ConflictException) { mutable.Add("ACADEMIC_SCOPE_INVALID"); }
        }
        var eligibility = new List<string>(mutable);
        var invitations = new List<string>(mutable);
        if (window is not null && policy is { IsValid: true })
        {
            var errors = team.AcademicScope is null ? TeamRules.EligibilityErrors(team.Members, policy, window.OrganizationId)
                : HybridTeamRules.EligibilityErrors(team.Members, policy, window.OrganizationId, team.AcademicScope);
            eligibility.AddRange(errors);
            invitations.AddRange(team.AcademicScope is null ? errors.Where(e => e != "TOO_FEW_MEMBERS")
                : HybridTeamRules.EligibilityErrors(team.Members, policy, window.OrganizationId, team.AcademicScope, false));
            if (team.Members.Count >= policy.MaxMembers) invitations.Add("TEAM_FULL");
            if (team.AcademicScope is not null && team.AcademicScope.Requirements.All(r => team.Members.Count(m => m.MajorId == r.MajorId) >= r.MaxMembers))
                invitations.Add("ALL_MAJOR_QUOTAS_FULL");
        }
        return new(team, policy, mutable.Distinct().ToArray(), eligibility.Distinct().ToArray(), invitations.Distinct().ToArray(), window is not null, unlocked);
    }

    private static WorkflowActionDto WithIssues(string code, IEnumerable<string> issues, params (bool Pass, string Reason)[] gates)
    {
        var action = Action(code, gates);
        var reasons = issues.Concat(action.Reasons).Distinct().ToArray();
        return new(code, reasons.Length == 0, reasons);
    }

    private async Task<IReadOnlyList<string>> DraftSemesterIssuesAsync(long userId, long semesterId, DateTime now, CancellationToken ct)
    {
        try
        {
            var actual = await projects.GetActiveRegistrationSemesterIdAsync(userId, now, ct);
            return actual == semesterId ? [] : ["REGISTRATION_WINDOW_UNAVAILABLE"];
        }
        catch (ConflictException) { return ["AMBIGUOUS_REGISTRATION_SEMESTER"]; }
    }

    public async Task<TeamWorkflowActionsDto> GetTeamActionsAsync(long userId, IReadOnlyCollection<string> tokenRoles,
        long teamId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var actor = await ReadActorAsync(userId, tokenRoles, ct);
        if (!actor.Student) throw new ForbiddenException("Only students can view team actions.");
        var state = await ReadTeamStateAsync(teamId, now.UtcDateTime, ct);
        var team = state.Team;
        var member = team.Members.SingleOrDefault(m => m.UserId == userId)
            ?? throw new ForbiddenException("Only current team members can view team actions.");
        var isLeader = member.IsLeader;
        var leader = (isLeader, "TEAM_LEADER_REQUIRED");
        var draftIssues = await DraftSemesterIssuesAsync(userId, team.SemesterId, now.UtcDateTime, ct);
        var activeProject = await projects.HasActiveProjectAsync(teamId, ct);
        var actions = new[]
        {
            Action("view_team"),
            WithIssues("edit_team", state.MutableIssues, leader),
            Action("configure_academic_scope", leader, (state.RosterUnlocked, "ROSTER_LOCKED"), (state.WindowOpen, "REGISTRATION_WINDOW_UNAVAILABLE"),
                (state.Policy is { IsValid: true }, "TEAM_POLICY_UNCONFIGURED")),
            WithIssues("refresh_eligibility", state.MutableIssues, leader),
            WithIssues("view_invitation_candidates", state.InvitationIssues, leader, (actor.Academic.HasEligibleStudentProfile, "STUDENT_PROFILE_INELIGIBLE")),
            WithIssues("invite_member", state.InvitationIssues, leader, (actor.Academic.HasEligibleStudentProfile, "STUDENT_PROFILE_INELIGIBLE")),
            Action("view_team_invitations", leader),
            WithIssues("leave_team", state.MutableIssues, (!isLeader, "TRANSFER_LEADERSHIP_FIRST")),
            WithIssues("remove_member", state.MutableIssues, leader, (team.Members.Any(m => m.UserId != userId), "NO_OTHER_MEMBER")),
            WithIssues("transfer_leadership", state.MutableIssues, leader,
                (team.Members.Any(m => m.UserId != userId && m.IsEligibleStudent), "NO_ELIGIBLE_REPLACEMENT")),
            WithIssues("create_project_draft", state.EligibilityIssues.Concat(draftIssues), leader,
                (!activeProject, "PROJECT_ALREADY_EXISTS"))
        };
        return new(now, teamId, team.SemesterId, team.Status, team.AcademicScope?.ConcurrencyToken,
            state.EligibilityIssues.Count == 0, state.EligibilityIssues, actions);
    }
}
