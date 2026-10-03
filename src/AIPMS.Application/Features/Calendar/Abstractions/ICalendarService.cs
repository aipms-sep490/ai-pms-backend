using AIPMS.Application.Features.Calendar.DTOs;

namespace AIPMS.Application.Features.Calendar.Abstractions;

public interface ICalendarService
{
    Task<CalendarResponseDto> GetAsync(DateTimeOffset from, DateTimeOffset to, string? cursor, int pageSize, string? sourceType, CancellationToken ct = default);
}
