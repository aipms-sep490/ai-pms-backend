using AIPMS.Domain.Teams;

namespace AIPMS.UnitTests.Domain.Teams;

public sealed class TeamRulesTests
{
    private static readonly TeamFormationPolicy Policy = new(2, 3, 24, "v1");
    private static TeamParticipant Student(long id, long major, bool leader = false) =>
        new(id, $"Student {id}", major, 1, true, leader);

    [Fact]
    public void Eligible_requires_size_single_major_and_one_leader() =>
        Assert.Empty(TeamRules.EligibilityErrors([Student(1, 10, true), Student(2, 10)], Policy, 1));

    [Fact]
    public void Mixed_majors_are_ineligible_even_in_the_same_organization() =>
        Assert.Contains("TEAM_MUST_BE_SINGLE_MAJOR",
            TeamRules.EligibilityErrors([Student(1, 10, true), Student(2, 20)], Policy, 1));

    [Fact]
    public void One_member_stays_forming() =>
        Assert.Contains("TOO_FEW_MEMBERS", TeamRules.EligibilityErrors([Student(1, 10, true)], Policy, 1));

    [Fact]
    public void Over_capacity_is_ineligible() =>
        Assert.Contains("TOO_MANY_MEMBERS", TeamRules.EligibilityErrors(
            [Student(1, 10, true), Student(2, 10), Student(3, 10), Student(4, 10)], Policy, 1));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Zero_or_two_leaders_are_ineligible(bool first, bool second) =>
        Assert.Contains("EXACTLY_ONE_LEADER_REQUIRED",
            TeamRules.EligibilityErrors([Student(1, 10, first), Student(2, 10, second)], Policy, 1));

    [Fact]
    public void Inactive_member_does_not_hide_a_legacy_mixed_major_roster() =>
        Assert.Equal(["INELIGIBLE_MEMBER", "TEAM_MUST_BE_SINGLE_MAJOR"],
            TeamRules.EligibilityErrors(
                [Student(1, 10, true), Student(2, 20) with { IsEligibleStudent = false }], Policy, 1));

    [Fact]
    public void Cross_organization_member_is_ineligible() =>
        Assert.Contains("INELIGIBLE_MEMBER", TeamRules.EligibilityErrors(
            [Student(1, 10, true), Student(2, 10) with { OrganizationId = 2 }], Policy, 1));

    [Fact]
    public void Missing_major_is_not_a_valid_single_major_team()
    {
        var errors = TeamRules.EligibilityErrors(
            [Student(1, 10, true) with { MajorId = null }, Student(2, 10) with { MajorId = null }], Policy, 1);
        Assert.Contains("INELIGIBLE_MEMBER", errors);
        Assert.Contains("TEAM_MUST_BE_SINGLE_MAJOR", errors);
        Assert.False(TeamRules.HasSameMajor(
            Student(1, 10) with { MajorId = null }, Student(2, 10) with { MajorId = null }));
    }

    [Fact]
    public void Inactive_same_major_member_still_blocks_eligibility() =>
        Assert.Equal(["INELIGIBLE_MEMBER"], TeamRules.EligibilityErrors(
            [Student(1, 10, true), Student(2, 10) with { IsEligibleStudent = false }], Policy, 1));

    [Fact]
    public void Empty_roster_cannot_be_single_major() =>
        Assert.Contains("TEAM_MUST_BE_SINGLE_MAJOR", TeamRules.EligibilityErrors([], Policy, 1));

    [Theory]
    [InlineData("DRAFT", false)]
    [InlineData("REVISION_REQUIRED", false)]
    [InlineData("REJECTED", false)]
    [InlineData("SUBMITTED", true)]
    [InlineData("UNDER_REVIEW", true)]
    [InlineData("APPROVED", true)]
    [InlineData("SUPERVISOR_PENDING", true)]
    [InlineData("ACTIVE", true)]
    [InlineData("FINAL_SUBMISSION", true)]
    [InlineData("COMPLETED", true)]
    [InlineData("ARCHIVED", true)]
    [InlineData("UNKNOWN", true)]
    public void Project_states_fail_closed_for_roster_changes(string status, bool expected) =>
        Assert.Equal(expected, TeamRules.ProjectLocksRoster(status));

    [Theory]
    [InlineData(0, 5, 24)]
    [InlineData(4, 3, 24)]
    [InlineData(2, 5, 0)]
    [InlineData(2, 5, 721)]
    public void Invalid_policy_fails_closed(int min, int max, int hours) =>
        Assert.False(new TeamFormationPolicy(min, max, hours, "v1").IsValid);

    [Fact]
    public void Size_policy_does_not_change_single_major_invariant()
    {
        var policy = new TeamFormationPolicy(2, 4, 24, "v2");
        Assert.True(policy.IsValid);
        Assert.Contains("TEAM_MUST_BE_SINGLE_MAJOR",
            TeamRules.EligibilityErrors([Student(1, 10, true), Student(2, 20)], policy, 1));
        Assert.Empty(TeamRules.EligibilityErrors(
            [Student(1, 20, true), Student(2, 20), Student(3, 20)], policy, 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Policy_version_is_required(string version) =>
        Assert.False(new TeamFormationPolicy(2, 4, 24, version).IsValid);

    [Fact]
    public void Expiry_is_exclusive_and_missing_expiry_fails_closed()
    {
        var now = DateTime.UtcNow;
        Assert.True(TeamRules.IsInvitationExpired(null, now));
        Assert.True(TeamRules.IsInvitationExpired(now, now));
        Assert.False(TeamRules.IsInvitationExpired(now.AddSeconds(1), now));
    }
}
