namespace AIPMS.Application.Features.Meetings.DTOs;

public sealed record MeetingDecisionDto(long Id, long MeetingId, string Content, long DecidedBy, DateTime DecidedAt);
public sealed record MeetingActionItemDto(long Id, long MeetingId, string Title, string? Description,
    long? AssigneeUserId, DateTime? DueAt, string Status, string ConcurrencyToken, long CreatedBy,
    DateTime CreatedAt, DateTime UpdatedAt);
public sealed record CreateMeetingDecisionRequest(string Content, string ConcurrencyToken);
public sealed record SaveMeetingActionItemRequest(string Title, string? Description, long? AssigneeUserId,
    DateTime? DueAt, string Status, string ConcurrencyToken);
