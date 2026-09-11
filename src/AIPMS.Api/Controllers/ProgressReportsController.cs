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
[Route("api/v1/progress-reports")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class ProgressReportsController(ISender sender) : ControllerBase
{
    [HttpGet("/api/v1/projects/{projectId:long}/progress-reports")]
    [ProducesResponseType<PagedResult<ProgressReportDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProgressReportDto>>> List(
        long projectId,
        [FromQuery] string? reportType = null,
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetProgressReportsQuery(
            projectId, reportType, status, from, to, page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType<ProgressReportDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProgressReportDetailDto>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProgressReportByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("/api/v1/projects/{projectId:long}/progress-reports")]
    [ProducesResponseType<ProgressReportDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProgressReportDto>> Create(
        long projectId,
        [FromBody] CreateProgressReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateProgressReportCommand(projectId, request), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<ProgressReportDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProgressReportDto>> Update(
        long id,
        [FromBody] UpdateProgressReportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateProgressReportCommand(id, request), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:long}/submit")]
    [ProducesResponseType<ProgressReportDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProgressReportDto>> Submit(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SubmitProgressReportCommand(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:long}/feedback")]
    [ProducesResponseType<ProgressReportFeedbackDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProgressReportFeedbackDto>> AddFeedback(
        long id,
        [FromBody] AddProgressReportFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new AddProgressReportFeedbackCommand(id, request), cancellationToken);
        return Created(string.Empty, result);
    }
}
