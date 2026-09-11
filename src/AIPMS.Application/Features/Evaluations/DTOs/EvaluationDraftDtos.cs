using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.DTOs;

public sealed record AssignEvaluatorRequest(long EvaluatorId, long ProjectPeriodId, string EvaluationType);
public sealed record RevokeEvaluatorRequest(string ConcurrencyToken, string Reason);
public sealed record SaveEvaluationDraftRequest(string ConcurrencyToken, string? Comments,
    IReadOnlyList<EvaluationScoreInput> Scores);
public sealed record EvaluationAssignmentDto(long Id, long ProjectId, long EvaluatorId, long RubricId,
    long ProjectPeriodId, long DepartmentId, string EvaluationType, string Status, long AssignedBy,
    DateTime AssignedAt, DateTime? RevokedAt, string ConcurrencyToken);
public sealed record EvaluationScoreDto(long RubricCriterionId, string Name, string? Description, decimal WeightPercent,
    decimal MaxScore, int SortOrder, bool IsRequired, decimal? Score, string? Comments);
public sealed record EvaluationDraftDto(long Id, long AssignmentId, long ProjectId, long EvaluatorId,
    long RubricId, string RubricName, long RootRubricId, int RubricVersion, string EvaluationType, string Status, string? Comments, decimal? TotalScore,
    decimal ScoreScale, string CalculationRule, IReadOnlyList<long> MissingCriterionIds,
    IReadOnlyList<long> MissingRequiredCriterionIds, string ConcurrencyToken, DateTime CreatedAt,
    DateTime UpdatedAt, IReadOnlyList<EvaluationScoreDto> Scores);

public static class EvaluationDraftDtoMapper
{
    public static EvaluationAssignmentDto ToDto(this EvaluationAssignmentRecord a) => new(a.Id,
        a.ProjectId, a.EvaluatorId, a.RubricId, a.PeriodId, a.DepartmentId, a.EvaluationType,
        a.Status, a.AssignedBy, Utc(a.AssignedAt), a.RevokedAt.HasValue ? Utc(a.RevokedAt.Value) : null,
        a.ConcurrencyToken);

    public static EvaluationDraftDto ToDto(this EvaluationDraftRecord e) => new(e.Id, e.AssignmentId,
        e.ProjectId, e.EvaluatorId, e.RubricId, e.RubricName, e.RootRubricId, e.RubricVersion, e.EvaluationType, e.Status, e.Comments, e.TotalScore,
        10m, "WEIGHTED_10_AWAY_FROM_ZERO_2DP_V1",
        e.Scores.Where(c => !c.Score.HasValue).Select(c => c.RubricCriterionId).ToArray(),
        e.Scores.Where(c => c.IsRequired && !c.Score.HasValue).Select(c => c.RubricCriterionId).ToArray(),
        e.ConcurrencyToken, Utc(e.CreatedAt), Utc(e.UpdatedAt),
        e.Scores.Select(c => new EvaluationScoreDto(c.RubricCriterionId, c.Name, c.Description, c.WeightPercent,
            c.MaxScore, c.SortOrder, c.IsRequired, c.Score, c.Comments)).ToArray());

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
