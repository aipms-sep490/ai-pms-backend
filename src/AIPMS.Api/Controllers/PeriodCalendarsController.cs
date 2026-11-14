using AIPMS.Application.Features.Semesters;
using AIPMS.Application.Features.Semesters.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/project-periods/{id:long}/calendar")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PeriodCalendarsController(IPeriodCalendarService calendars) : ControllerBase
{
    [HttpGet]
    public Task<PeriodCalendarDto> Get(long id, CancellationToken ct) => calendars.Get(id, ct);

    [HttpPut]
    public Task<PeriodCalendarDto> Save(long id, SavePeriodCalendarRequest input, CancellationToken ct) => calendars.Save(id, input, ct);

    [HttpPost("load-vn-holidays")]
    public Task<PeriodCalendarDto> LoadVietnamHolidays(long id, LoadVietnamHolidaysRequest input, CancellationToken ct) => calendars.LoadVietnamHolidays(id, input, ct);
}
