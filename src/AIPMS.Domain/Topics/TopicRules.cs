using AIPMS.Domain.Teams;

namespace AIPMS.Domain.Topics;

public static class TopicRules
{
    public static IReadOnlyList<string> ScopeIssues(TeamAcademicScope scope, TeamFormationPolicy? policy = null) =>
        HybridTeamRules.ConfigurationErrors(scope, policy ?? new(1, int.MaxValue, 24, "structure-only"));

    public static IReadOnlyList<string> PublicationIssues(string? problem, string? objectives, string? output,
        string? domain, IReadOnlyList<string> technologies, IReadOnlyList<string> keywords)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(problem)) issues.Add("PROBLEM_STATEMENT_REQUIRED");
        if (string.IsNullOrWhiteSpace(objectives)) issues.Add("OBJECTIVES_REQUIRED");
        if (string.IsNullOrWhiteSpace(output)) issues.Add("EXPECTED_OUTPUT_REQUIRED");
        if (string.IsNullOrWhiteSpace(domain) || technologies.Count == 0 || keywords.Count == 0)
            issues.Add("PROPOSAL_TAGS_REQUIRED");
        return issues;
    }
}
