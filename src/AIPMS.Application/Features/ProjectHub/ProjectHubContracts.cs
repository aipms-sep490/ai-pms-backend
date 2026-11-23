namespace AIPMS.Application.Features.ProjectHub;

public sealed record CreateSpecVersionRequest(string? SubmissionNote, string? Changelog);

public sealed record SpecVersionDto(
    long Id,
    long DeliverableId,
    int VersionNumber,
    string Status,
    string? SubmissionNote,
    string? Changelog,
    long SubmittedBy,
    DateTime SubmittedAt);

public sealed record SpecVersionCompareDto(
    long DeliverableId,
    SpecVersionDto FromVersion,
    SpecVersionDto ToVersion,
    IReadOnlyList<string> AddedFiles,
    IReadOnlyList<string> RemovedFiles,
    IReadOnlyList<string> UnchangedFiles);

public sealed record CreatePinnedResourceRequest(string ResourceType, string Title, string Url);

public sealed record PinnedResourceDto(
    long Id,
    long ProjectId,
    string ResourceType,
    string Title,
    string Url,
    long CreatedBy,
    DateTime CreatedAt);

public sealed record PostChannelMessageRequest(string Body);

public sealed record HubChannelMessageDto(
    long Id,
    long ConversationId,
    string ChannelScope,
    long Sequence,
    long SenderId,
    string Body,
    DateTime CreatedAt);

public sealed record HubChannelDto(
    long ConversationId,
    long ProjectId,
    string ChannelScope,
    IReadOnlyList<HubChannelMessageDto> Messages);

public sealed record ConvertMessageToTaskRequest(long? MilestoneId = null, string? Title = null);

public sealed record ChatConvertedTaskDto(
    long TaskId,
    long MilestoneId,
    long SourceMessageId,
    string Title,
    string Status,
    bool AlreadyExisted);

public sealed record ConvertMessageToDecisionRequest(long? MeetingId = null, string? Content = null);

public sealed record ChatConvertedDecisionDto(
    long DecisionId,
    long MeetingId,
    long SourceMessageId,
    string Content,
    bool AlreadyExisted);

public sealed record CreateAnchoredCommentRequest(
    long? EvidenceId,
    string AnchorRef,
    string Severity,
    string Content);

public sealed record AnchoredCommentDto(
    long Id,
    long ProjectId,
    long? GateInstanceId,
    long? EvidenceId,
    string AnchorRef,
    string Severity,
    string Status,
    string Content,
    long CreatedBy,
    long? ResolvedBy,
    DateTime CreatedAt,
    DateTime? ResolvedAt);

public sealed record LinkEvidenceToRubricRequest(long EvidenceId, long RubricCriterionId);

public sealed record EvidenceRubricLinkDto(
    long Id,
    long ProjectId,
    long EvidenceId,
    long RubricCriterionId,
    long CreatedBy,
    DateTime CreatedAt);

public sealed record MemberWeeklyActivityDto(
    long UserId,
    string FullName,
    long? MajorId,
    IReadOnlyList<int> WeeklyCounts,
    int TotalActions,
    DateTime? LastActivityAt,
    int? DaysSinceLastActivity,
    bool IsSilentMember);

public sealed record InternalContributionHeatmapDto(
    long ProjectId,
    int Weeks,
    DateTime GeneratedAtUtc,
    IReadOnlyList<MemberWeeklyActivityDto> Members,
    IReadOnlyList<long> SilentMemberIds);

public sealed record HealthPulseSignalsDto(
    decimal? EvidenceReadyPercent,
    int TotalMandatoryGateRequirements,
    int BlockedOver48hCount,
    int WeeklyReportsLast14Days,
    decimal? InterdisciplinaryBalanceRatio);

public sealed record ProjectHealthPulseDto(
    long ProjectId,
    string Status,
    string Summary,
    HealthPulseSignalsDto Signals,
    DateTime EvaluatedAtUtc);

public sealed record ProjectAuditTimelineEntryDto(
    long Id,
    long? ActorUserId,
    string Action,
    string EntityType,
    string? EntityId,
    string Outcome,
    DateTime OccurredAt);
