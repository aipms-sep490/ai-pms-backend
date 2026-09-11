using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record RubricCriterionDto(long Id, long CriterionId, string Name, string? Description,
    decimal WeightPercent, decimal MaxScore, int SortOrder, bool IsRequired);
public sealed record RubricDto(long Id, long? DepartmentId, long? AcademicSemesterId, string Code,
    string Name, string? Description, string Status, bool IsActive, long RootRubricId, int Version,
    string ConcurrencyToken, bool CanEdit, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<RubricCriterionDto> Criteria);
public sealed record CreateRubricRequest(long DepartmentId, long AcademicSemesterId, string Code,
    string Name, string? Description, IReadOnlyList<RubricCriterionInput> Criteria);
public sealed record UpdateRubricRequest(string Name, string? Description,
    IReadOnlyList<RubricCriterionInput> Criteria, string ConcurrencyToken);
public sealed record ChangeRubricStatusRequest(string ConcurrencyToken);
public sealed record CreateRubricVersionRequest(string Code, string ConcurrencyToken);

public static class RubricDtoMapper
{
    public static RubricDto ToDto(this RubricRecord r) => new(r.Id, r.DepartmentId,
        r.AcademicSemesterId, r.Code, r.Name, r.Description, r.Status,
        r.Status == RubricStatuses.Published, r.RootRubricId, r.Version, r.ConcurrencyToken,
        r.Status == RubricStatuses.Draft && !r.IsReferenced,
        DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc), DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc),
        r.Criteria.Select(c => new RubricCriterionDto(c.Id, c.CriterionId, c.Name, c.Description,
            c.WeightPercent, c.MaxScore, c.SortOrder, c.IsRequired)).ToArray());
}
