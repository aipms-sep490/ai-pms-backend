using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Appeals;

public sealed class AppealSettings { public bool Enabled { get; set; } }
public sealed record RecordAppealPolicyRequest(int ExpectedVersion, string ApprovalReference);
public sealed record AppealPolicyDto(long Id, long ProjectId, int Version, string ApprovalReference, long RecordedBy, DateTime RecordedAt);
public sealed record AppealWindowTarget(string TargetType, long? StudentId, int ResultVersion);
public sealed record OpenAppealWindowRequest(long PolicyId, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, IReadOnlyList<AppealWindowTarget> Targets);
public sealed record AppealWindowDto(long Id, long PolicyId, string Status, DateTime OpensAt, DateTime ClosesAt, string ConcurrencyToken,
    IReadOnlyList<AppealWindowTarget> Targets);
public sealed record AppealTokenRequest(string ConcurrencyToken);
public sealed record CreateCorrectionRequest(string Source, long PolicyId, long? WindowId, string TargetType, long? StudentId,
    int ResultVersion, long ComponentId, string Reason);
public sealed record AssignAppealReviewerRequest(string ConcurrencyToken, long ReviewerId);
public sealed record ReviewAppealRequest(string ConcurrencyToken, string Proposal, string Reason, IReadOnlyList<ScoreAmendmentInput> Amendments);
public sealed record AppealDecisionRequest(string ConcurrencyToken, string Decision, string Reason, string? ConfirmationToken);
public sealed record AppealFileDto(long FileId, string FileName, string ContentType, long SizeBytes, string Sha256, string DownloadUrl);
public sealed record AppealEventDto(long Id, string Action, long ActorId, DateTime At, string? Reason,
    long? BundleId, IReadOnlyList<ScoreAmendmentInput> Amendments);
public sealed record CorrectionRequestDto(long Id, long ProjectId, string Source, string TargetType, long? StudentId,
    int ResultVersion, long ComponentId, string Scope, long? MajorId, string Status, long RequestedBy, long? ReviewerId,
    string Reason, string? Proposal, string? ReviewReason, string? DecisionReason, string ConcurrencyToken,
    IReadOnlyList<string> AllowedActions, IReadOnlyList<string> Blockers, IReadOnlyList<AppealFileDto> Files);
public sealed record AppealPreviewDto(string ConfirmationToken, IReadOnlyList<ResultRevisionSummary> AffectedResults);
public sealed record AppealReviewContextDto(long RequestId, long ComponentId, string Scope, long? MajorId, long? StudentId,
    string CalculationVersion, IReadOnlyList<AppealReviewEvaluationDto> Evaluations, IReadOnlyList<ScoreAmendmentInput> Amendments);
public sealed record AppealReviewEvaluationDto(long Id, long AssignmentId, long EvaluatorId, long RubricId, string RubricName,
    int RubricVersion, decimal? TotalScore, IReadOnlyList<EvaluationScoreRecord> Scores);
