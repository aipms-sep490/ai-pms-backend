using AIPMS.Domain.Teams;

namespace AIPMS.UnitTests.Domain;

public sealed class HybridTeamRulesTests
{
    private static readonly TeamFormationPolicy Policy = new(2, 4, 24, "v1", 2);
    private static readonly TeamAcademicScope Scope = new("INTERDISCIPLINARY", null, 1,
        [new(10, 1, 2, "Software"), new(20, 1, 2, "Analysis")], Guid.NewGuid());
    private static readonly TeamParticipant[] Members = [new(1, "Leader", 10, 1, true, true), new(2, "Member", 20, 1, true, false)];

    [Fact]
    public void Verified_members_in_each_required_major_pass() => Assert.Empty(HybridTeamRules.EligibilityErrors(Members, Policy, 1, Scope));

    [Fact]
    public void Total_size_does_not_replace_each_major_minimum()
    {
        var members = Members.Select(m => m with { MajorId = 10 }).ToArray();
        Assert.Contains("MAJOR_MIN_MEMBERS:20", HybridTeamRules.EligibilityErrors(members, Policy, 1, Scope));
    }

    [Fact]
    public void Configuration_rejects_infeasible_or_duplicate_quotas()
    {
        Assert.Contains("MAJOR_QUOTAS_INFEASIBLE", HybridTeamRules.ConfigurationErrors(Scope with
            { Requirements = [new(10, 3, 4, "Software"), new(20, 3, 4, "Analysis")] }, Policy));
        Assert.Contains("MAJOR_REQUIREMENTS_INVALID", HybridTeamRules.ConfigurationErrors(Scope with
            { Requirements = [new(10, 1, 2, "Software"), new(10, 1, 2, "Analysis")] }, Policy));
    }

    [Fact]
    public void Joining_can_be_incomplete_but_must_not_exceed_quota()
    {
        Assert.Empty(HybridTeamRules.EligibilityErrors([Members[0]], Policy, 1, Scope, false));
        Assert.Contains("MAJOR_MAX_MEMBERS:10", HybridTeamRules.EligibilityErrors(
            [Members[0], new(3, "A", 10, 1, true, false), new(4, "B", 10, 1, true, false)], Policy, 1, Scope, false));
    }

    [Fact]
    public void Unverified_or_other_organization_members_do_not_fill_quota()
    {
        var errors = HybridTeamRules.EligibilityErrors([Members[0], Members[1] with { OrganizationId = 2 }], Policy, 1, Scope);
        Assert.Contains("INELIGIBLE_MEMBER", errors); Assert.Contains("MAJOR_MIN_MEMBERS:20", errors);
    }

    [Fact]
    public void Explicit_single_major_uses_primary_major_even_when_interdisciplinary_minimum_is_two()
    {
        var single = Scope with { ProjectMode = "SINGLE_MAJOR", PrimaryMajorId = 10, Requirements = [new(10, 2, 4, "Software")] };
        Assert.Empty(HybridTeamRules.EligibilityErrors(Members.Select(m => m with { MajorId = 10 }).ToArray(), Policy, 1, single));
        Assert.Contains("MEMBER_MAJOR_NOT_ALLOWED", HybridTeamRules.EligibilityErrors(Members, Policy, 1, single));
    }
}
