namespace AIPMS.Application.Features.Meetings.DTOs;

public sealed record CreateMeetingSeriesRequest(CreateMeetingRequest Meeting, string Recurrence = "NONE", int OccurrenceCount = 1);
public sealed record MeetingSeriesDto(Guid SeriesId, string Recurrence, IReadOnlyList<MeetingDto> Occurrences);
public sealed record RespondMeetingRequest(string Status, string ConcurrencyToken);
public sealed record MeetingRsvpDto(long MeetingId, long UserId, string Status, string ConcurrencyToken);
public sealed record LockMeetingNotesRequest(string ConcurrencyToken);
public sealed record MeetingNotesVersionDto(long Id, long MeetingId, int Version, bool IsLocked, string? MeetingNotes,
    string? Minutes, string? Decisions, string? Blockers, long ChangedBy, DateTime ChangedAt, MeetingGovernanceSnapshotDto Governance);
public sealed record MeetingGovernanceSnapshotDto(IReadOnlyList<MeetingDecisionSnapshotDto> Decisions,
    IReadOnlyList<MeetingActionDefinitionDto> Actions, IReadOnlyList<ProjectActionDefinitionDto> ProjectActions);
public sealed record MeetingDecisionSnapshotDto(long Id, string Content, long DecidedBy, DateTime DecidedAt);
public sealed record MeetingActionDefinitionDto(long Id, string Title, string? Description, long? AssigneeUserId, DateTime? DueAt);
public sealed record ProjectActionDefinitionDto(long Id, string Title, string? Description, long? OwnerId, long? TaskId, long? MilestoneId, DateTime? DueAt);
public sealed record MeetingCollaborationDto(long MeetingId, string ConcurrencyToken, Guid? SeriesId, int? OccurrenceNumber,
    string Recurrence, int NotesVersion, DateTime? NotesLockedAt, long? NotesLockedBy, bool CanLockNotes,
    MeetingRsvpDto? MyRsvp, IReadOnlyList<MeetingEmailDeliveryDto> EmailDeliveries, bool HasMoreEmailDeliveries);
public sealed record MeetingEmailDeliveryDto(long UserId, string NotificationType, string Status, int AttemptCount, DateTime? SentAt);
