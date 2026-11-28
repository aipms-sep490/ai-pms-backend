namespace AIPMS.Application.Features.Results.DTOs;

public sealed record ResultAssignmentInput(long AssignmentId, decimal WeightPercent);
public sealed record ConfigureResultPolicyRequest(decimal PassThreshold, IReadOnlyList<ResultAssignmentInput> Assignments, string? ConcurrencyToken);
public sealed record ResultPolicyDto(long ProjectId, decimal PassThreshold, string ConcurrencyToken, bool IsLocked, IReadOnlyList<ResultAssignmentInput> Assignments);
public sealed record PublishProjectResultRequest(string ConfirmationToken);
public sealed record ResultContributionDto(long AssignmentId, long EvaluationId, long EvaluatorId, long RubricId,
    decimal WeightPercent, decimal Score, string EvaluationConcurrencyToken, string? EvaluatorName = null);
public sealed record ResultWarningDto(string Code, string Message, long? ComponentId = null, decimal? Score = null, decimal? Threshold = null);
public sealed record ProjectResultPreviewDto(long ProjectId, bool CanPublish, IReadOnlyList<string> Blockers,
    decimal? TotalScore, decimal? PassThreshold, string? Outcome, string ConfirmationToken, IReadOnlyList<ResultContributionDto> Contributions,
    AIPMS.Application.Features.Evaluations.DTOs.ResultBreakdownDto? Breakdown = null,
    string? OutcomeDetail = null,
    IReadOnlyList<ResultWarningDto>? Warnings = null);
public sealed record ProjectResultDto(long Id, long ProjectId, long FinalSubmissionId, decimal TotalScore, decimal PassThreshold,
    string Outcome, string CalculationRule, long PublishedBy, DateTime PublishedAt, string PolicyConcurrencyToken,
    IReadOnlyList<ResultContributionDto> Contributions, long? SchemeId = null, long? PolicyVersionId = null, string? FrozenInputs = null,
    AIPMS.Application.Features.Evaluations.DTOs.ResultBreakdownDto? Breakdown = null,
    int Version = 1, long? RevisionId = null, int? SupersedesVersion = null,
    string? OutcomeDetail = null);
public sealed record BatchPublishResultRequest(string ConfirmationToken);
public sealed record BatchPublishResultDto(ProjectResultDto ProjectResult,
    IReadOnlyList<AIPMS.Application.Features.Evaluations.DTOs.StudentResultDto> StudentResults);
public sealed record RecordDefenseAbsenceRequest(string Reason, string? MinutesReference = null);
public sealed record StudentDefenseAbsenceDto(long Id, long ProjectId, long StudentId, long RecordedBy,
    string Reason, string? MinutesReference, DateTime RecordedAt);
