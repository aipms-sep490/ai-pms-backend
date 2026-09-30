using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ActionItems.Commands;
using AIPMS.Application.Features.ActionItems.DTOs;
using AIPMS.Application.Features.ActionItems.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:long}/action-items")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class ProjectActionItemsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ProjectActionItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectActionItemDto>>> List(
        long projectId,
        [FromQuery] string? sourceType = null,
        [FromQuery] long? meetingId = null,
        [FromQuery] long? progressReportId = null,
        [FromQuery] string? status = null,
        [FromQuery] long? ownerId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetProjectActionItemsQuery(
            projectId, sourceType, meetingId, progressReportId, status, ownerId, page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType<ProjectActionItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectActionItemDto>> GetById(
        long projectId,
        long id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProjectActionItemByIdQuery(projectId, id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType<ProjectActionItemDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProjectActionItemDto>> Create(
        long projectId,
        [FromBody] CreateProjectActionItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateProjectActionItemCommand(projectId, request), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { projectId, id = result.Id }, result);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<ProjectActionItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectActionItemDto>> UpdateDetails(
        long projectId,
        long id,
        [FromBody] UpdateProjectActionItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateProjectActionItemCommand(projectId, id, request), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:long}/status")]
    [ProducesResponseType<ProjectActionItemDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectActionItemDto>> UpdateStatus(
        long projectId,
        long id,
        [FromBody] UpdateProjectActionItemStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateProjectActionItemStatusCommand(projectId, id, request), cancellationToken);
        return Ok(result);
    }
}
