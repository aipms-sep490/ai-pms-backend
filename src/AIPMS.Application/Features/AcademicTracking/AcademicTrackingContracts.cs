using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.AcademicTracking;

public sealed record EvidenceReviewTarget(long EvidenceId, long? SourceVersionId, string SourceVersion,
    long LatestReviewId, bool CanReview, string? DenialReason);
public sealed record ReviewEvidenceRequest(long? SourceVersionId, string ExpectedSourceVersion,
    long ExpectedLatestReviewId, string Decision, string Reason);
public sealed record EvidenceReviewDto(long Id, long EvidenceId, long? SourceVersionId, string SourceVersion,
    long ReviewerId, string Decision, string Reason, DateTime ReviewedAt);
public sealed record SaveCheckpointRequest(string Title, long? MajorId, DateTimeOffset PlannedAt,
    string? ConcurrencyToken = null);
public sealed record CheckpointEvidenceInput(long EvidenceId, long ReviewId);
public sealed record DecideCheckpointRequest(string ConcurrencyToken, string Decision, string Reason,
    IReadOnlyList<CheckpointEvidenceInput> Evidence);
public sealed record CheckpointDecisionDto(long Id, string Decision, string Reason, long DecidedBy,
    DateTime DecidedAt, IReadOnlyList<CheckpointEvidenceInput> Evidence);
public sealed record CheckpointDto(long Id, long ProjectId, string Title, long? MajorId, DateTime PlannedAt,
    string ConcurrencyToken, bool IsOverdue, bool CanManage, IReadOnlyList<CheckpointDecisionDto> Decisions,
    bool TrackingOnly = true, bool BlocksAcademicWorkflow = false);
public sealed record CommitteeMemberInput(long UserId, long MajorId);
public sealed record CreateCommitteeRequest(string Name, IReadOnlyList<CommitteeMemberInput> Members);
public sealed record CommitteeDto(long Id, long ProjectId, string Name, IReadOnlyList<CommitteeMemberInput> Members,
    bool GrantsEvaluatorAuthority = false);
public sealed record ScheduleDefenseRequest(long CommitteeId, DateTimeOffset StartAt, DateTimeOffset EndAt, string Room);
public sealed record CancelDefenseRequest(string ConcurrencyToken, string Reason);
public sealed record DefenseSessionDto(long Id, long ProjectId, long CommitteeId, DateTime StartAt, DateTime EndAt,
    string Room, string Status, string ConcurrencyToken, string? CancellationReason, bool CanCancel);
