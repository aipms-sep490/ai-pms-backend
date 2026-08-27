namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record RubricCriterionDto(long Id, long CriterionId, string Code, string Name,
    string? Description, decimal WeightPercent, decimal MaxScore, int SortOrder, bool IsRequired);

public sealed record RubricDto(long Id, long? DepartmentId, long? AcademicSemesterId, string Code,
    string Name, string? Description, int VersionNumber, string ApprovalStatus, bool IsActive,
    long CreatedBy, DateTime CreatedAt, DateTime UpdatedAt, long? ApprovedBy,
    DateTime? ApprovedAt, IReadOnlyList<RubricCriterionDto> Criteria);

public sealed record EvaluationDto(long Id, long ProjectId, long EvaluatorId, long RubricId,
    int RubricVersion, string EvaluationType, string Status, decimal? TotalScore,
    string? Comments, string? EvidenceSummary, DateTime? EvaluatedAt, long? FinalizedBy,
    DateTime? FinalizedAt, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record EvaluationScoreDto(long RubricCriterionId, long CriterionId, string Code,
    string Name, decimal WeightPercent, decimal MaxScore, bool IsRequired, int SortOrder,
    decimal? Score, string? Comments);

public sealed record EvaluationDetailDto(EvaluationDto Evaluation,
    IReadOnlyList<EvaluationScoreDto> Scores);

public sealed record CreateRubricData(long? DepartmentId, long? AcademicSemesterId, string Code,
    string Name, string? Description, int VersionNumber, long ActorId);
public sealed record UpdateRubricData(string Name, string? Description);
public sealed record UpsertRubricCriterionData(long CriterionId, decimal WeightPercent,
    decimal MaxScore, int SortOrder, bool IsRequired);

