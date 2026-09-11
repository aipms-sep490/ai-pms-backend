using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.Commands;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.Meetings.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/meetings")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class MeetingsController(ISender sender) : ControllerBase
{
    [HttpGet("/api/v1/projects/{projectId:long}/meetings")]
    [ProducesResponseType<PagedResult<MeetingDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MeetingDto>>> List(
        long projectId,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetMeetingsQuery(
            projectId, status, from, to, page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType<MeetingDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeetingDetailDto>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetMeetingByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("/api/v1/projects/{projectId:long}/meetings")]
    [ProducesResponseType<MeetingDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MeetingDto>> Create(
        long projectId,
        [FromBody] CreateMeetingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateMeetingCommand(projectId, request), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<MeetingDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeetingDto>> Update(
        long id,
        [FromBody] UpdateMeetingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateMeetingCommand(id, request), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Cancels a scheduled meeting, transitioning its status to CANCELLED.
    /// </summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType<MeetingDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeetingDto>> Cancel(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CancelMeetingCommand(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Soft-cancels a scheduled meeting by transitioning its status to CANCELLED to preserve history.
    /// Does not physically remove records from the database.
    /// </summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        long id,
        CancellationToken cancellationToken)
    {
        await sender.Send(new CancelMeetingCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:long}/notes")]
    [ProducesResponseType<MeetingDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeetingDto>> UpdateNotes(
        long id,
        [FromBody] UpdateMeetingNotesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateMeetingNotesCommand(id, request), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:long}/participants")]
    [ProducesResponseType<MeetingParticipantDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MeetingParticipantDto>> AddParticipant(
        long id,
        [FromBody] AddMeetingParticipantRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new AddMeetingParticipantCommand(id, request), cancellationToken);
        return Created(string.Empty, result);
    }

    [HttpDelete("{id:long}/participants/{userId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveParticipant(
        long id,
        long userId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveMeetingParticipantCommand(id, userId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:long}/feedback")]
    [ProducesResponseType<MeetingFeedbackDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MeetingFeedbackDto>> AddFeedback(
        long id,
        [FromBody] AddMeetingFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new AddMeetingFeedbackCommand(id, request), cancellationToken);
        return Created(string.Empty, result);
    }
}
