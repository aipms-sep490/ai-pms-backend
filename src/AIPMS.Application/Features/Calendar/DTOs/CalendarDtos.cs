namespace AIPMS.Application.Features.Calendar.DTOs;

public sealed record CalendarItemDto(string SourceType, long SourceId, long ProjectId, string ProjectCode, string ProjectTitle,
    string Title, DateTime? StartAt, DateTime? EndAt, DateTime DueAt, string Status, string? DeepLink);
public sealed record CalendarResponseDto(IReadOnlyList<CalendarItemDto> Items, string? NextCursor, bool HasMore, bool Complete);
