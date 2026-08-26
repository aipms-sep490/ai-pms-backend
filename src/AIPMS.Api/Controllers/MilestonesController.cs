using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Milestones.Commands;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.Milestones.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/milestones")]
public sealed class MilestonesController(ISender sender) : ControllerBase
{
    [HttpGet("{id}")]
    [ProducesResponseType<MilestoneDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MilestoneDto>> GetById(
        long id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMilestoneByIdQuery(id), cancellationToken));

    [HttpGet("project/{projectId}")]
    [ProducesResponseType<IReadOnlyList<MilestoneDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MilestoneDto>>> GetByProject(
        long projectId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMilestonesQuery(projectId), cancellationToken));

    [HttpGet("project/{projectId}/progress")]
    [ProducesResponseType<IReadOnlyList<MilestoneProgressDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MilestoneProgressDto>>> GetProgress(
        long projectId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMilestoneProgressQuery(projectId), cancellationToken));

    [HttpPost]
    [ProducesResponseType<MilestoneDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MilestoneDto>> Create(
        [FromBody] CreateMilestoneCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    [ProducesResponseType<MilestoneDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MilestoneDto>> Update(
        long id,
        [FromBody] UpdateMilestoneCommand request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateMilestoneCommand(
            id,
            request.Title,
            request.Description,
            request.StartDate,
            request.DueDate,
            request.Status,
            request.SortOrder);

        return Ok(await sender.Send(command, cancellationToken));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        long id,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteMilestoneCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("project/{projectId}/reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reorder(
        long projectId,
        [FromBody] IReadOnlyList<MilestoneReorderItem> items,
        CancellationToken cancellationToken)
    {
        await sender.Send(new ReorderMilestonesCommand(projectId, items), cancellationToken);
        return NoContent();
    }
}
