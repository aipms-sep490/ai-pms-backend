using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Queries;
using AIPMS.Application.Features.Tasks.Commands;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Application.Features.Tasks.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tasks")]
public sealed class TasksController(ISender sender) : ControllerBase
{
    [HttpGet("{id}")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskDto>> GetById(
        long id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTaskByIdQuery(id), cancellationToken));

    [HttpGet("project/{projectId}")]
    [ProducesResponseType<PagedResult<TaskDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TaskDto>>> GetByProject(
        long projectId,
        [FromQuery] long? milestoneId,
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] long? assigneeUserId,
        [FromQuery] string? search,
        [FromQuery] DateTime? dueFrom,
        [FromQuery] DateTime? dueTo,
        [FromQuery] bool? isOverdue,
        [FromQuery] bool? isBlocked,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new GetTasksQuery(
            projectId,
            milestoneId,
            status,
            priority,
            assigneeUserId,
            search,
            dueFrom,
            dueTo,
            isOverdue,
            isBlocked,
            page,
            pageSize);

        return Ok(await sender.Send(query, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<TaskDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaskDto>> Create(
        [FromBody] CreateTaskCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskDto>> Update(
        long id,
        [FromBody] UpdateTaskCommand request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTaskCommand(
            id,
            request.MilestoneId,
            request.ParentTaskId,
            request.Title,
            request.Description,
            request.Priority,
            request.StartAt,
            request.DueAt);

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
        await sender.Send(new DeleteTaskCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id}/assignees")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaskDto>> SetAssignees(
        long id,
        [FromBody] IReadOnlyList<long> assigneeUserIds,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new SetTaskAssigneesCommand(id, assigneeUserIds), cancellationToken));

    [HttpPost("dependency")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TaskDto>> AddDependency(
        [FromBody] AddTaskDependencyCommand command,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(command, cancellationToken));

    [HttpDelete("{id}/dependency/{dependsOnTaskId}")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskDto>> RemoveDependency(
        long id,
        long dependsOnTaskId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RemoveTaskDependencyCommand(id, dependsOnTaskId), cancellationToken));

    [HttpPut("{id}/status")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaskDto>> UpdateStatus(
        long id,
        [FromBody] UpdateTaskStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new UpdateTaskStatusCommand(id, request.NewStatus, request.Reason), cancellationToken));

    [HttpGet("{id}/history")]
    [ProducesResponseType<IReadOnlyList<TaskStatusHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TaskStatusHistoryDto>>> GetHistory(
        long id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTaskStatusHistoryQuery(id), cancellationToken));

    [HttpGet("project/{projectId}/overdue-blocked")]
    [ProducesResponseType<OverdueBlockedTasksDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OverdueBlockedTasksDto>> GetOverdueBlocked(
        long projectId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetOverdueBlockedTasksQuery(projectId), cancellationToken));

    [HttpGet("project/{projectId}/timeline")]
    [ProducesResponseType<ProjectTimelineDataDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectTimelineDataDto>> GetTimeline(
        long projectId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTimelineDataQuery(projectId), cancellationToken));

    [HttpGet("project/{projectId}/progress-summary")]
    [ProducesResponseType<ProjectProgressSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectProgressSummaryDto>> GetProgressSummary(
        long projectId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetProjectProgressSummaryQuery(projectId), cancellationToken));
}

public sealed record UpdateTaskStatusRequest(string NewStatus, string? Reason);
