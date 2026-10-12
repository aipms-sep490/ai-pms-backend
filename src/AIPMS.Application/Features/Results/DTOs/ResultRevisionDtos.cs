using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Results.DTOs;

public sealed record ScoreAmendmentInput(long EvaluationId, long CriterionId, decimal Score, string Reason);
public sealed record ResultRevisionSummary(long? RevisionId, int Version, int? SupersedesVersion, long? StudentId,
    decimal TotalScore, string Outcome, long PublishedBy, DateTime PublishedAt, string? Reason, long? BundleId);
public sealed record RevisionCalculation(string FrozenInputs, decimal TotalScore, ResultBreakdownDto Breakdown,
    IReadOnlyList<ResultContributionDto> Contributions);
