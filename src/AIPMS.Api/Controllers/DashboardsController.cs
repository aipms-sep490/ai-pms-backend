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
[ProducesResponseType<ProblemDetails>(422)]
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

    [HttpGet("department")]
    public Task<PortfolioDashboardDto> Department(CancellationToken ct, [FromQuery] long? semesterId = null,
        [FromQuery] long? majorId = null, [FromQuery] string? status = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        sender.Send(new GetDepartmentDashboardQuery(semesterId, majorId, status, search, page, pageSize), ct);

    [HttpGet("admin")]
    public Task<PortfolioDashboardDto> Admin(CancellationToken ct, [FromQuery] long? semesterId = null,
        [FromQuery] long? departmentId = null, [FromQuery] long? majorId = null, [FromQuery] string? status = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        sender.Send(new GetAdminDashboardQuery(semesterId, departmentId, majorId, status, search, page, pageSize), ct);

    [HttpGet("portfolio/export")]
    public async Task<IActionResult> Export(CancellationToken ct, [FromQuery] long? semesterId = null,
        [FromQuery] long? departmentId = null, [FromQuery] long? majorId = null, [FromQuery] string? status = null,
        [FromQuery] string? search = null, [FromQuery] string format = "csv")
    {
        var result = await sender.Send(new ExportPortfolioDashboardQuery(semesterId, departmentId, majorId, status, search, format), ct);
        return File(result.Content, "text/csv; charset=utf-8", result.FileName);
    }
}
