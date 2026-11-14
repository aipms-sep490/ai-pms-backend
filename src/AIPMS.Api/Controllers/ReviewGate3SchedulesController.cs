using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.AcademicTracking.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/project-periods/{id:long}/review-gates/r3/schedule")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReviewGate3SchedulesController(IReviewGate3ScheduleService schedules) : ControllerBase
{
    [HttpPost("preview")]
    public Task<ReviewGate3SchedulePreviewDto> Preview(long id, AutoSchedulePreviewRequest input, CancellationToken ct) =>
        schedules.Preview(id, input, ct);

    [HttpPost("apply")]
    public Task<IReadOnlyList<ReviewGate3ScheduleDto>> Apply(long id, ReviewGate3ScheduleApplyRequest input, CancellationToken ct) =>
        schedules.Apply(id, input, ct);

    [HttpGet]
    public Task<IReadOnlyList<ReviewGate3ScheduleDto>> List(long id, CancellationToken ct) => schedules.List(id, ct);

    [HttpPut("{gateInstanceId:long}")]
    public Task<ReviewGate3ScheduleDto> Change(long id, long gateInstanceId, ReviewGate3ScheduleChangeRequest input,
        CancellationToken ct) => schedules.Change(id, gateInstanceId, input, ct);
}
