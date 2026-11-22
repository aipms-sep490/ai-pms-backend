using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Meetings.Video;

public sealed record MeetingTranscriptSegmentDto(
    long Id,
    long MeetingId,
    long? MeetingVideoSessionId,
    long? SpeakerUserId,
    string SpeakerName,
    string Language,
    string Source,
    string Content,
    bool IsHighlighted,
    string? ClientSegmentId,
    DateTime SpokenAt,
    long CreatedBy);

public sealed record MeetingSpeakerStatDto(
    long? SpeakerUserId,
    string SpeakerName,
    int SegmentCount,
    int WordCount,
    int HighlightCount,
    double SharePercent);

public sealed record MeetingTranscriptFeedDto(
    long MeetingId,
    int TotalSegments,
    long? LastSegmentId,
    IReadOnlyList<MeetingTranscriptSegmentDto> Segments,
    IReadOnlyList<MeetingSpeakerStatDto> SpeakerStats,
    MeetingAiInsightDto? LatestInsight,
    bool AiAvailable,
    bool AudioSttConfigured);

public sealed record CreateTranscriptSegmentRequest(
    string Content,
    string? Language = "vi-VN",
    string? Source = "WEB_SPEECH",
    string? SpeakerName = null,
    bool IsHighlighted = false,
    string? ClientSegmentId = null,
    DateTime? SpokenAt = null);

public sealed record ToggleTranscriptHighlightRequest(bool IsHighlighted);

public sealed record MeetingAiTopicDto(
    string Title,
    string Summary,
    IReadOnlyList<string> Speakers);

public sealed record MeetingAiActionItemDto(
    string Title,
    string? Description,
    string? OwnerName,
    long? AssigneeUserId,
    DateTime? DueAt,
    string Priority,
    string? EvidenceQuote,
    long? SourceSegmentId);

public sealed record MeetingAiAgendaItemDto(
    string Item,
    string Status,
    string Note);

public sealed record MeetingAiSpeakerInsightDto(
    string SpeakerName,
    long? UserId,
    string Summary,
    IReadOnlyList<string> KeyContributions);

public sealed record MeetingAiNextMeetingDto(
    string Title,
    string? Purpose,
    string? SuggestedTimeNote);

public sealed record MeetingAiInsightDto(
    long Id,
    long MeetingId,
    string InsightType,
    string Status,
    string? ModelVersion,
    int TranscriptSegmentCount,
    long? LastSegmentId,
    string ExecutiveSummary,
    string? CurrentTopic,
    string FormattedMinutes,
    IReadOnlyList<MeetingAiTopicDto> KeyTopics,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<MeetingAiActionItemDto> ActionItems,
    IReadOnlyList<string> BlockersAndRisks,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<MeetingAiAgendaItemDto> AgendaCoverage,
    IReadOnlyList<MeetingAiSpeakerInsightDto> SpeakerInsights,
    IReadOnlyList<string> SuggestedNextQuestions,
    MeetingAiNextMeetingDto? NextMeetingSuggestion,
    long CreatedBy,
    string CreatedByName,
    DateTime CreatedAt);

public sealed record GenerateMeetingAiSummaryRequest(
    string Mode = "FULL_MINUTES",
    int? RecentMinutes = null,
    string? FocusPrompt = null);

public sealed record MeetingAiCitationDto(
    long SegmentId,
    string SpeakerName,
    DateTime SpokenAt,
    string Excerpt);

public sealed record AskMeetingAiRequest(
    string Question,
    int? RecentMinutes = null);

public sealed record MeetingAiAnswerDto(
    long MeetingId,
    string Question,
    string Answer,
    IReadOnlyList<MeetingAiCitationDto> Citations,
    IReadOnlyList<string> FollowUpQuestions,
    string Status,
    DateTime GeneratedAt);

public sealed record MeetingAiCoachRequest(
    string Mode = "AGENDA_CHECK",
    int? RecentMinutes = null);

public sealed record MeetingAiCoachFindingDto(
    string Category,
    string Severity,
    string Title,
    string Detail,
    string? RelatedEntity);

public sealed record MeetingAiCoachDto(
    long MeetingId,
    string Mode,
    string Headline,
    string Summary,
    IReadOnlyList<MeetingAiCoachFindingDto> Findings,
    IReadOnlyList<string> SuggestedPrompts,
    string? DraftText,
    string Status,
    DateTime GeneratedAt);
