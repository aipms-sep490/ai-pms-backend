using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.AcademicTracking.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/project-periods/{id:long}/defense-sessions/auto-schedule")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AutoScheduleController(IAutoScheduleService schedules) : ControllerBase
{
    [HttpPost("preview")]
    public Task<AutoSchedulePreviewDto> Preview(long id, AutoSchedulePreviewRequest input, CancellationToken ct) =>
        schedules.Preview(id, input, ct);

    [HttpPost("apply")]
    public Task<AutoScheduleApplyDto> Apply(long id, AutoScheduleApplyRequest input, CancellationToken ct) =>
        schedules.Apply(id, input, ct);
}
