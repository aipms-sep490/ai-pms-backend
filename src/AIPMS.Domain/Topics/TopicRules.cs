using System.Globalization;
using System.Text;
using AIPMS.Domain.Teams;

namespace AIPMS.Domain.Topics;

public static class TopicRules
{
    public const double SimilarityWarningThreshold = 0.5;

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

    public static IReadOnlyList<string> ProposalCompletenessIssues(string? proposalSource, string? problemStatement,
        string? objectives, string? expectedOutput, string? domain,
        IReadOnlyList<string>? technologies, IReadOnlyList<string>? keywords)
    {
        if (!string.Equals(proposalSource, "STUDENT_PROPOSAL", StringComparison.Ordinal))
            return [];

        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(problemStatement))
            issues.Add("PROPOSAL_CONTENT_INCOMPLETE:problemStatement");
        if (string.IsNullOrWhiteSpace(objectives))
            issues.Add("PROPOSAL_CONTENT_INCOMPLETE:objectives");
        if (string.IsNullOrWhiteSpace(expectedOutput))
            issues.Add("PROPOSAL_CONTENT_INCOMPLETE:expectedOutput");
        if (string.IsNullOrWhiteSpace(domain))
            issues.Add("PROPOSAL_CONTENT_INCOMPLETE:domain");
        if (technologies is null || technologies.Count == 0 || technologies.All(string.IsNullOrWhiteSpace))
            issues.Add("PROPOSAL_CONTENT_INCOMPLETE:technologies");
        if (keywords is null || keywords.Count == 0 || keywords.All(string.IsNullOrWhiteSpace))
            issues.Add("PROPOSAL_CONTENT_INCOMPLETE:keywords");
        return issues;
    }

    public static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;

            var mapped = ch switch
            {
                'đ' or 'Đ' => 'd',
                _ => char.ToLowerInvariant(ch)
            };

            if (char.IsWhiteSpace(mapped))
            {
                if (!previousWasSpace)
                {
                    sb.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                sb.Append(mapped);
                previousWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    public static HashSet<string> NormalizeKeywords(IEnumerable<string>? keywords)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (keywords is null) return set;
        foreach (var raw in keywords)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = NormalizeTitle(part);
                if (!string.IsNullOrEmpty(normalized))
                    set.Add(normalized);
            }
        }
        return set;
    }

    public static double CalculateSimilarity(IEnumerable<string>? keywordsA, string? domainA,
        IEnumerable<string>? keywordsB, string? domainB)
    {
        var setA = NormalizeKeywords(keywordsA);
        var setB = NormalizeKeywords(keywordsB);
        var keywordScore = Jaccard(setA, setB);

        var normDomainA = NormalizeTitle(domainA);
        var normDomainB = NormalizeTitle(domainB);
        if (!string.IsNullOrEmpty(normDomainA) && !string.IsNullOrEmpty(normDomainB))
        {
            var withDomainA = new HashSet<string>(setA, StringComparer.Ordinal) { $"domain:{normDomainA}" };
            var withDomainB = new HashSet<string>(setB, StringComparer.Ordinal) { $"domain:{normDomainB}" };
            var combinedScore = Jaccard(withDomainA, withDomainB);
            return Math.Round(Math.Max(keywordScore, combinedScore), 2);
        }

        return Math.Round(keywordScore, 2);
    }

    private static double Jaccard(HashSet<string> setA, HashSet<string> setB)
    {
        if (setA.Count == 0 || setB.Count == 0) return 0d;
        var intersection = setA.Count(setB.Contains);
        var union = setA.Count + setB.Count - intersection;
        return union == 0 ? 0d : (double)intersection / union;
    }
}

