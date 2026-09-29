using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Disciplines.DTOs;

namespace AIPMS.Application.Features.Disciplines.Validators;

public static class DisciplineValidation
{
    public static readonly IReadOnlySet<string> Sources = new HashSet<string>(StringComparer.Ordinal)
        { "TASK", "DELIVERABLE", "MEETING", "PROGRESS_REPORT", "FILE" };

    public static void Responsibilities(IReadOnlyList<ResponsibilityInput>? items)
    {
        if (items is null || items.Count > 100 || items.Any(x => x is null || string.IsNullOrWhiteSpace(x.Content)
            || x.Content.Trim().Length > 2000 || x.SortOrder < 0) || items.Select(x => x.SortOrder).Distinct().Count() != items.Count)
            Invalid("items", "Use up to 100 nonempty responsibilities (2000 characters), with distinct nonnegative sort orders.");
    }

    public static void Disciplines(IReadOnlyList<TaskDisciplineInput>? items, bool interdisciplinary)
    {
        if (items is null || items.Count > 100 || items.Any(x => x is null || x.MajorId <= 0 || x.Role is not ("PRIMARY" or "SUPPORTING"))
            || items.Select(x => x.MajorId).Distinct().Count() != items.Count || items.Count(x => x.Role == "PRIMARY") > 1
            || interdisciplinary && items.Count(x => x.Role == "PRIMARY") != 1)
            Invalid("items", "Use distinct majors with PRIMARY/SUPPORTING roles; interdisciplinary tasks require exactly one PRIMARY.");
    }

    public static void Evidence(CreateProjectEvidenceRequest request)
    {
        Source(request.SourceType);
        if (request.SourceId <= 0 || request.MajorId is <= 0 || request.Notes?.Trim().Length > 2000)
            Invalid("evidence", "Provide a positive source/major ID and notes of at most 2000 characters.");
    }

    public static void Source(string? source)
    {
        if (source is null || !Sources.Contains(source)) Invalid("sourceType", "Unknown evidence source type.");
    }

    public static void Invalid(string field, string message) => throw new ValidationException(new Dictionary<string, string[]> { [field] = [message] });
}
