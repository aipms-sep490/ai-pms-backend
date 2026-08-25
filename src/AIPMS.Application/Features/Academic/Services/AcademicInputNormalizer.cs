namespace AIPMS.Application.Features.Academic.Services;

internal static class AcademicInputNormalizer
{
    public static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    public static string NormalizeName(string name) =>
        name.Trim();

    public static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
