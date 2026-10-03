using AIPMS.Application.Features.Calendar.Abstractions;
using AIPMS.Application.Features.Calendar.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
public sealed class CalendarController(ICalendarService service) : ControllerBase
{
    [HttpGet("api/v1/calendar")]
    [ProducesResponseType<CalendarResponseDto>(StatusCodes.Status200OK)]
    public Task<CalendarResponseDto> Get([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] string? cursor, [FromQuery] int pageSize = 50, [FromQuery] string? sourceType = null, CancellationToken ct = default)
        => service.GetAsync(from, to, cursor, pageSize, sourceType, ct);
}
