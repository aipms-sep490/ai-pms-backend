namespace AIPMS.Domain.Teams;

public sealed record TeamFormationPolicy(
    int MinMembers, int MaxMembers, int InvitationHours, string Version, int MinDistinctMajors = 1)
{
    public bool IsValid => MinMembers >= 1 && MaxMembers >= MinMembers
        && InvitationHours is >= 1 and <= 720 && !string.IsNullOrWhiteSpace(Version)
        && MinDistinctMajors >= 1 && MinDistinctMajors <= MaxMembers;
}

public sealed record TeamParticipant(
    long UserId, string FullName, long? MajorId, long? OrganizationId,
    bool IsEligibleStudent, bool IsLeader);

public static class TeamRules
{
    // Single-major membership is checked for single-major policies (MinDistinctMajors <= 1).
    public static bool HasSameMajor(TeamParticipant student, TeamParticipant leader) =>
        student.MajorId.HasValue && leader.MajorId.HasValue && student.MajorId == leader.MajorId;

    public static bool ProjectLocksRoster(string status) =>
        status is not ("DRAFT" or "REVISION_REQUIRED" or "REJECTED");

    public static IReadOnlyList<string> EligibilityErrors(
        IReadOnlyList<TeamParticipant> members, TeamFormationPolicy policy, long organizationId)
    {
        var errors = new List<string>();
        if (!policy.IsValid)
        {
            errors.Add("TEAM_POLICY_INVALID");
            return errors;
        }
        if (members.Count < policy.MinMembers) errors.Add("TOO_FEW_MEMBERS");
        if (members.Count > policy.MaxMembers) errors.Add("TOO_MANY_MEMBERS");
        if (members.Count(m => m.IsLeader) != 1) errors.Add("EXACTLY_ONE_LEADER_REQUIRED");
        if (members.Any(m => !m.IsEligibleStudent || m.MajorId is null
            || m.OrganizationId != organizationId)) errors.Add("INELIGIBLE_MEMBER");

        var eligibleMembersWithMajor = members
            .Where(m => m.IsEligibleStudent && m.MajorId.HasValue && m.OrganizationId == organizationId)
            .ToList();
        var distinctMajors = eligibleMembersWithMajor
            .Select(m => m.MajorId)
            .Distinct()
            .Count();

        if (policy.MinDistinctMajors <= 1)
        {
            if (distinctMajors != 1
                || members.Where(m => m.MajorId.HasValue).Select(m => m.MajorId).Distinct().Count() > 1)
            {
                errors.Add("TEAM_MUST_BE_SINGLE_MAJOR");
            }
        }
        else
        {
            if (distinctMajors < policy.MinDistinctMajors)
            {
                errors.Add("TOO_FEW_DISTINCT_MAJORS");
            }
        }
        return errors;
    }

    public static bool IsInvitationExpired(DateTime? expiresAt, DateTime now) =>
        expiresAt is null || expiresAt <= now;
}
