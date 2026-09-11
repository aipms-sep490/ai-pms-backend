namespace AIPMS.Application.Features.Evaluations.Models;

public sealed record EvaluationScoreInput(long RubricCriterionId, decimal? Score, string? Comments);
public sealed record EvaluationScoreRecord(long RubricCriterionId, string Name, string? Description, decimal WeightPercent,
    decimal MaxScore, int SortOrder, bool IsRequired, decimal? Score, string? Comments);
public sealed record EvaluationPreview(decimal? Total, IReadOnlyList<long> MissingCriterionIds,
    IReadOnlyList<long> MissingRequiredCriterionIds);
public sealed record EvaluationActor(long Id, long? DepartmentId, bool IsAdmin, bool IsStaff, bool IsLecturer);
public sealed record EvaluationProject(long Id, long SemesterId, long OrganizationId, string Status,
    bool ActiveScope, IReadOnlyList<long> DepartmentIds);
public sealed record EvaluationPeriod(long Id, long SemesterId, long? RubricId, bool IsOpen);
public sealed record EvaluationAssignmentRecord(long Id, long ProjectId, long EvaluatorId, long RubricId,
    long PeriodId, long DepartmentId, string EvaluationType, string Status, long AssignedBy,
    DateTime AssignedAt, DateTime? RevokedAt, string ConcurrencyToken);
public sealed record EvaluationDraftRecord(long Id, long AssignmentId, long ProjectId, long EvaluatorId,
    long RubricId, string RubricName, long RootRubricId, int RubricVersion, string EvaluationType, string Status, string? Comments, decimal? TotalScore,
    string ConcurrencyToken, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<EvaluationScoreRecord> Scores);
