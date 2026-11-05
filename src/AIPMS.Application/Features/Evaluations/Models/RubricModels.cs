using System;
using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Evaluations.Models;

public static class RubricStatuses
{
    public const string Draft = "DRAFT";
    public const string PendingApproval = "PENDING_APPROVAL";
    public const string Published = "PUBLISHED";
    public const string Retired = "RETIRED";
}

public sealed record RubricActor(long UserId, bool IsAdmin, long? DepartmentId, long? OrganizationId = null, bool IsStaff = false);
public sealed record RubricScope(long DepartmentId, long OrganizationId, string SemesterStatus);
public sealed record RubricApprovalRecord(long Id, long RubricId, long SubmittedBy, DateTime SubmittedAt,
    long? DecidedBy, DateTime? DecidedAt, string? Decision, string? Reason);

public sealed record RubricCriterionLevelInput(int LevelOrder, string Label, string Descriptor,
    decimal? ScoreFrom, decimal? ScoreTo);

public sealed record RubricCriterionInput(string Name, string? Description, decimal WeightPercent,
    decimal? MaxScore, int SortOrder, bool IsRequired)
{
    public string? LearningOutcome { get; init; }
    public string? EvidenceGuidance { get; init; }
    public IReadOnlyList<RubricCriterionLevelInput> Levels { get; init; } = [];
    public IReadOnlyList<RubricCriterionInput> Children { get; init; } = [];
}

public sealed record RubricCriterionRecord(long Id, long CriterionId, string Name, string? Description,
    decimal WeightPercent, decimal? MaxScore, int SortOrder, bool IsRequired)
{
    public long? ParentId { get; init; }
    public decimal EffectiveWeightPercent { get; init; }
    public string? CriterionCode { get; init; }
    public string? LearningOutcome { get; init; }
    public string? EvidenceGuidance { get; init; }
    public IReadOnlyList<RubricCriterionLevelDto> Levels { get; init; } = [];
}

public sealed record RubricRecord(long Id, long? DepartmentId, long? AcademicSemesterId,
    string Code, string Name, string? Description, string Status, long RootRubricId, int Version,
    string ConcurrencyToken, bool IsReferenced, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<RubricCriterionRecord> Criteria)
{
    public long? MajorId { get; init; }
    public string? ProjectMode { get; init; }
    public string? EvaluationStage { get; init; }
    public string? EvaluationScope { get; init; }
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public string? ApprovalReference { get; init; }
}

public sealed record RubricFilter(long? DepartmentId, long? AcademicSemesterId, string? Status,
    string? Search, int Page, int PageSize)
{
    public long? MajorId { get; init; }
    public string? ProjectMode { get; init; }
    public string? EvaluationStage { get; init; }
    public string? EvaluationScope { get; init; }
}
