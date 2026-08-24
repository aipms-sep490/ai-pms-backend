using AIPMS.Application.Common.Models;
namespace AIPMS.Application.Features.Evaluations.DTOs;
public sealed record EvaluationDto(long Id,long ProjectId,long EvaluatorId,string EvaluationType,string Status,decimal? TotalScore,string? Comments,DateTime? EvaluatedAt);
public sealed record EvaluationScoreDto(long CriterionId,string Code,string Name,decimal WeightPercent,decimal MaxScore,decimal? Score,string? Comments);
public sealed record EvaluationDetailDto(EvaluationDto Evaluation,IReadOnlyList<EvaluationScoreDto> Scores);
