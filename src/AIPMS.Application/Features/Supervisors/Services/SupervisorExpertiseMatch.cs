namespace AIPMS.Application.Features.Supervisors.Services;

public static class SupervisorExpertiseMatch
{
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
    public static bool Matches(string expertise, string code, string name) =>
        Normalize(expertise) == Normalize(code) || Normalize(expertise) == Normalize(name);
}
