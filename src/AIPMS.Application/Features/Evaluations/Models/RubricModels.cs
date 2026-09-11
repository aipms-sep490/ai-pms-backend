namespace AIPMS.Application.Features.Evaluations.Models;

public static class RubricStatuses
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
    public const string Retired = "RETIRED";
}

public sealed record RubricActor(long UserId, bool IsAdmin, long? DepartmentId);
public sealed record RubricScope(long DepartmentId, long OrganizationId, string SemesterStatus);
public sealed record RubricCriterionInput(string Name, string? Description, decimal WeightPercent,
    decimal MaxScore, int SortOrder, bool IsRequired);
public sealed record RubricCriterionRecord(long Id, long CriterionId, string Name, string? Description,
    decimal WeightPercent, decimal MaxScore, int SortOrder, bool IsRequired);
public sealed record RubricRecord(long Id, long? DepartmentId, long? AcademicSemesterId,
    string Code, string Name, string? Description, string Status, long RootRubricId, int Version,
    string ConcurrencyToken, bool IsReferenced, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<RubricCriterionRecord> Criteria);
public sealed record RubricFilter(long? DepartmentId, long? AcademicSemesterId, string? Status,
    string? Search, int Page, int PageSize);
