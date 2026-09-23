using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;

namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record RubricCriterionDto(long Id, long CriterionId, string Name, string? Description,
    decimal WeightPercent, decimal? MaxScore, int SortOrder, bool IsRequired)
{
    public long? ParentId { get; init; }
    public decimal EffectiveWeightPercent { get; init; }
    public IReadOnlyList<RubricCriterionDto> Children { get; init; } = [];
}
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
    public static RubricDto ToDto(this RubricRecord r)
    {
        var ordered = RubricHierarchy.WithEffectiveWeights(r.Criteria);
        var byParent = ordered.ToLookup(c => c.ParentId);
        var mapped = new Dictionary<long, RubricCriterionDto>();
        foreach (var c in ordered.Reverse())
            mapped.Add(c.Id, new(c.Id, c.CriterionId, c.Name, c.Description, c.WeightPercent, c.MaxScore,
                c.SortOrder, c.IsRequired)
            {
                ParentId = c.ParentId,
                EffectiveWeightPercent = c.EffectiveWeightPercent,
                Children = byParent[c.Id].OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => mapped[x.Id]).ToArray()
            });

        return new(r.Id, r.DepartmentId, r.AcademicSemesterId, r.Code, r.Name, r.Description, r.Status,
            r.Status == RubricStatuses.Published, r.RootRubricId, r.Version, r.ConcurrencyToken,
            r.Status == RubricStatuses.Draft && !r.IsReferenced,
            DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc), DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc),
            byParent[null].OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(c => mapped[c.Id]).ToArray());
    }
}
