using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressReports.Commands;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.ProgressReports.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:long}/reporting-cycles")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class ReportingCyclesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ReportingCycleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReportingCycleDto>>> List(
        long projectId,
        [FromQuery] string? reportType = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetReportingCyclesQuery(
            projectId, reportType, from, to, page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType<ReportingCycleDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReportingCycleDto>> GetById(
        long projectId,
        long id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetReportingCycleByIdQuery(projectId, id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType<ReportingCycleDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ReportingCycleDto>> Create(
        long projectId,
        [FromBody] CreateReportingCycleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateReportingCycleCommand(projectId, request), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { projectId, id = result.Id }, result);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<ReportingCycleDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReportingCycleDto>> Update(
        long projectId,
        long id,
        [FromBody] UpdateReportingCycleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateReportingCycleCommand(projectId, id, request), cancellationToken);
        return Ok(result);
    }
}
