using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Application.Features.Dashboards.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/dashboards")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
public sealed class DashboardsController(ISender sender) : ControllerBase
{
    [HttpGet("student")]
    public Task<StudentDashboardDto> Student(CancellationToken ct, [FromQuery] long? semesterId = null) =>
        sender.Send(new GetStudentDashboardQuery(semesterId), ct);

    [HttpGet("supervisor")]
    public Task<SupervisorDashboardDto> Supervisor(CancellationToken ct, [FromQuery] long? semesterId = null,
        [FromQuery] string? status = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        sender.Send(new GetSupervisorDashboardQuery(semesterId, status, search, page, pageSize), ct);
}
