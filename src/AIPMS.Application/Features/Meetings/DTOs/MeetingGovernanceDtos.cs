namespace AIPMS.Application.Features.Meetings.DTOs;

public sealed record MeetingDecisionDto(long Id, long MeetingId, string Content, long DecidedBy, DateTime DecidedAt,
    long? SupersededByDecisionId = null, string? DecidedByName = null);
public sealed record MeetingActionItemDto(long Id, long MeetingId, string Title, string? Description,
    long? AssigneeUserId, DateTime? DueAt, string Status, string ConcurrencyToken, long CreatedBy,
    DateTime CreatedAt, DateTime UpdatedAt,
    string? AssigneeName = null, string? CreatedByName = null);
public sealed record CreateMeetingDecisionRequest(string Content, string ConcurrencyToken);
public sealed record SaveMeetingActionItemRequest(string Title, string? Description, long? AssigneeUserId,
    DateTime? DueAt, string Status, string ConcurrencyToken);
