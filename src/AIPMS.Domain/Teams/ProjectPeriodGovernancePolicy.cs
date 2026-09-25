namespace AIPMS.Domain.Teams;

public static class ProjectPeriodGovernancePolicy
{
    public const string DefaultModes = "SINGLE_MAJOR,INTERDISCIPLINARY";
    public const string DefaultSources = "PUBLISHED_TOPIC,STUDENT_PROPOSAL";

    public static bool IsValid(string? csv, bool modes) => csv is null ||
        (csv.Length is > 0 and <= 200 && csv.Split(',').All(value =>
            (modes ? DefaultModes : DefaultSources).Split(',').Contains(value.Trim().ToUpperInvariant())));

    public static string Normalize(string csv) => string.Join(',', csv.Split(',')
        .Select(value => value.Trim().ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal));

    public static bool Allows(string csv, string value) => csv.Split(',')
        .Contains(value, StringComparer.OrdinalIgnoreCase);
}
