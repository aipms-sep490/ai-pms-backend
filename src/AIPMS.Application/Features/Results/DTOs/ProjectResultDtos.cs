namespace AIPMS.Application.Features.Results.DTOs;

public sealed record ResultAssignmentInput(long AssignmentId, decimal WeightPercent);
public sealed record ConfigureResultPolicyRequest(decimal PassThreshold, IReadOnlyList<ResultAssignmentInput> Assignments, string? ConcurrencyToken);
public sealed record ResultPolicyDto(long ProjectId, decimal PassThreshold, string ConcurrencyToken, bool IsLocked, IReadOnlyList<ResultAssignmentInput> Assignments);
public sealed record PublishProjectResultRequest(string ConfirmationToken);
public sealed record ResultContributionDto(long AssignmentId, long EvaluationId, long EvaluatorId, long RubricId,
    decimal WeightPercent, decimal Score, string EvaluationConcurrencyToken);
public sealed record ProjectResultPreviewDto(long ProjectId, bool CanPublish, IReadOnlyList<string> Blockers,
    decimal? TotalScore, decimal? PassThreshold, string? Outcome, string ConfirmationToken, IReadOnlyList<ResultContributionDto> Contributions);
public sealed record ProjectResultDto(long Id, long ProjectId, long FinalSubmissionId, decimal TotalScore, decimal PassThreshold,
    string Outcome, string CalculationRule, long PublishedBy, DateTime PublishedAt, string PolicyConcurrencyToken,
    IReadOnlyList<ResultContributionDto> Contributions);
