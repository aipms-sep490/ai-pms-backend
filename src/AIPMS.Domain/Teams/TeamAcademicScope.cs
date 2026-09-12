namespace AIPMS.Domain.Teams;

public sealed record MajorRequirement(long MajorId, int MinMembers, int MaxMembers, string Responsibility);

public sealed record TeamAcademicScope(string ProjectMode, long? PrimaryMajorId,
    long LeadDepartmentId, IReadOnlyList<MajorRequirement> Requirements, Guid ConcurrencyToken);

public static class HybridTeamRules
{
    public static IReadOnlyList<string> ConfigurationErrors(TeamAcademicScope scope, TeamFormationPolicy policy)
    {
        var errors = new List<string>();
        if (scope.ProjectMode is not ("SINGLE_MAJOR" or "INTERDISCIPLINARY")) errors.Add("INVALID_PROJECT_MODE");
        if (scope.LeadDepartmentId <= 0) errors.Add("LEAD_DEPARTMENT_REQUIRED");
        var requirements = scope.Requirements;
        if (requirements.Count == 0 || requirements.Select(r => r.MajorId).Distinct().Count() != requirements.Count)
            errors.Add("MAJOR_REQUIREMENTS_INVALID");
        if (requirements.Any(r => r.MajorId <= 0 || r.MinMembers < 1 || r.MaxMembers < r.MinMembers
            || r.MaxMembers > policy.MaxMembers || string.IsNullOrWhiteSpace(r.Responsibility) || r.Responsibility.Length > 1000))
            errors.Add("MAJOR_QUOTA_INVALID");
        if (requirements.Sum(r => (long)r.MinMembers) > policy.MaxMembers
            || requirements.Sum(r => (long)r.MaxMembers) < policy.MinMembers)
            errors.Add("MAJOR_QUOTAS_INFEASIBLE");
        if (scope.ProjectMode == "SINGLE_MAJOR" && (requirements.Count != 1
            || scope.PrimaryMajorId != requirements[0].MajorId)) errors.Add("PRIMARY_MAJOR_REQUIRED");
        if (scope.ProjectMode == "INTERDISCIPLINARY" && (scope.PrimaryMajorId is not null
            || requirements.Count < Math.Max(2, policy.MinDistinctMajors))) errors.Add("INTERDISCIPLINARY_MAJORS_REQUIRED");
        return errors;
    }

    public static IReadOnlyList<string> EligibilityErrors(IReadOnlyList<TeamParticipant> members,
        TeamFormationPolicy policy, long organizationId, TeamAcademicScope scope, bool requireMinimum = true)
    {
        var errors = ConfigurationErrors(scope, policy).ToList();
        if (!policy.IsValid) errors.Add("TEAM_POLICY_INVALID");
        if (requireMinimum && members.Count < policy.MinMembers) errors.Add("TOO_FEW_MEMBERS");
        if (members.Count > policy.MaxMembers) errors.Add("TOO_MANY_MEMBERS");
        if (members.Count(m => m.IsLeader) != 1) errors.Add("EXACTLY_ONE_LEADER_REQUIRED");
        if (members.Any(m => !m.IsEligibleStudent || m.MajorId is null || m.OrganizationId != organizationId))
            errors.Add("INELIGIBLE_MEMBER");
        if (members.Any(m => !scope.Requirements.Any(r => r.MajorId == m.MajorId))) errors.Add("MEMBER_MAJOR_NOT_ALLOWED");
        foreach (var requirement in scope.Requirements)
        {
            var count = members.Count(m => m.IsEligibleStudent && m.MajorId == requirement.MajorId && m.OrganizationId == organizationId);
            if (requireMinimum && count < requirement.MinMembers) errors.Add($"MAJOR_MIN_MEMBERS:{requirement.MajorId}");
            if (count > requirement.MaxMembers) errors.Add($"MAJOR_MAX_MEMBERS:{requirement.MajorId}");
        }
        return errors.Distinct().ToArray();
    }
}
