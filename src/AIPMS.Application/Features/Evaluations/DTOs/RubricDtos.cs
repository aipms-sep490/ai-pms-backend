using System;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;

namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record RubricCriterionLevelDto(int LevelOrder, string Label, string Descriptor,
    decimal? ScoreFrom, decimal? ScoreTo);

public sealed record RubricCriterionDto(long Id, long CriterionId, string Name, string? Description,
    decimal WeightPercent, decimal? MaxScore, int SortOrder, bool IsRequired)
{
    public long? ParentId { get; init; }
    public decimal EffectiveWeightPercent { get; init; }
    public string? CriterionCode { get; init; }
    public string? LearningOutcome { get; init; }
    public string? EvidenceGuidance { get; init; }
    public IReadOnlyList<RubricCriterionLevelDto> Levels { get; init; } = [];
    public IReadOnlyList<RubricCriterionDto> Children { get; init; } = [];
}
public sealed record RubricDto(long Id, long? DepartmentId, long? AcademicSemesterId, string Code,
    string Name, string? Description, string Status, bool IsActive, long RootRubricId, int Version,
    string ConcurrencyToken, bool CanEdit, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<RubricCriterionDto> Criteria)
{
    public long? MajorId { get; init; }
    public string? ProjectMode { get; init; }
    public string? EvaluationStage { get; init; }
    public string? EvaluationScope { get; init; }
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public string? ApprovalReference { get; init; }
    public bool CanSubmitForApproval { get; init; }
    public bool CanApprove { get; init; }
    public bool CanReject { get; init; }
}
public sealed record CreateRubricRequest(long DepartmentId, long AcademicSemesterId, string Code,
    string Name, string? Description, IReadOnlyList<RubricCriterionInput> Criteria)
{
    public long? MajorId { get; init; }
    public string? ProjectMode { get; init; }
    public string? EvaluationStage { get; init; }
    public string? EvaluationScope { get; init; }
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
}
public sealed record UpdateRubricRequest(string Name, string? Description,
    IReadOnlyList<RubricCriterionInput> Criteria, string ConcurrencyToken)
{
    public long? MajorId { get; init; }
    public string? ProjectMode { get; init; }
    public string? EvaluationStage { get; init; }
    public string? EvaluationScope { get; init; }
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
}
public sealed record ChangeRubricStatusRequest(string ConcurrencyToken);
public sealed record CreateRubricVersionRequest(string Code, string ConcurrencyToken);
public sealed record SubmitRubricForApprovalRequest(string ConcurrencyToken);
public sealed record ApproveRubricRequest(string ConcurrencyToken, string? ApprovalReference = null);
public sealed record RejectRubricRequest(string ConcurrencyToken, string Reason);

public static class RubricDtoMapper
{
    public static RubricDto ToDto(this RubricRecord r, bool canSubmitForApproval = false, bool canApprove = false, bool canReject = false)
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
                CriterionCode = c.CriterionCode,
                LearningOutcome = c.LearningOutcome,
                EvidenceGuidance = c.EvidenceGuidance,
                Levels = c.Levels,
                Children = byParent[c.Id].OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => mapped[x.Id]).ToArray()
            });

        return new(r.Id, r.DepartmentId, r.AcademicSemesterId, r.Code, r.Name, r.Description, r.Status,
            r.Status == RubricStatuses.Published, r.RootRubricId, r.Version, r.ConcurrencyToken,
            r.Status == RubricStatuses.Draft && !r.IsReferenced,
            DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc), DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc),
            byParent[null].OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(c => mapped[c.Id]).ToArray())
        {
            MajorId = r.MajorId,
            ProjectMode = r.ProjectMode,
            EvaluationStage = r.EvaluationStage,
            EvaluationScope = r.EvaluationScope,
            EffectiveFrom = r.EffectiveFrom,
            EffectiveTo = r.EffectiveTo,
            ApprovalReference = r.ApprovalReference,
            CanSubmitForApproval = canSubmitForApproval,
            CanApprove = canApprove,
            CanReject = canReject
        };
    }
}
