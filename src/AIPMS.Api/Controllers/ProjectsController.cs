using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Projects.Commands;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Queries;
using AIPMS.Application.Features.Projects.Queries.GetProjectLifecycle;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController(ISender sender) : ControllerBase
{
    [HttpGet("lifecycle")]
    [ProducesResponseType<ProjectLifecycleDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectLifecycleDto>> GetLifecycle(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetProjectLifecycleQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProjectDto>> CreateDraft(
        [FromBody] CreateProjectDraftRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateProjectDraftCommand(
            request.Title,
            request.Description,
            request.Objectives,
            request.ProblemStatement,
            request.ExpectedOutput,
            request.RequiredMajorIds,
            request.Domain,
            request.Technologies,
            request.Keywords);

        var result = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> UpdateDraft(
        long id,
        [FromBody] UpdateProjectDraftRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateProjectDraftCommand(
            id,
            request.ConcurrencyToken,
            request.Title,
            request.Description,
            request.Objectives,
            request.ProblemStatement,
            request.ExpectedOutput,
            request.RequiredMajorIds,
            request.Domain,
            request.Technologies,
            request.Keywords);

        return Ok(await sender.Send(command, cancellationToken));
    }

    [HttpPut("{id}/majors")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> SetMajors(
        long id,
        [FromBody] SetProjectMajorsRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SetProjectMajorsCommand(
            id,
            request.ConcurrencyToken,
            request.RequiredMajorIds);

        return Ok(await sender.Send(command, cancellationToken));
    }

    [HttpGet("{id}")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> GetById(
        long id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetProjectByIdQuery(id), cancellationToken));

    [HttpGet]
    [ProducesResponseType<PagedResult<ProjectSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectSummaryDto>>> GetProjects(
        [FromQuery] string? status,
        [FromQuery] long? teamId,
        [FromQuery] long? semesterId,
        [FromQuery] long? majorId,
        [FromQuery] string? tag,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new GetProjectsQuery(status, teamId, semesterId, majorId, tag, search, page, pageSize);
        return Ok(await sender.Send(query, cancellationToken));
    }

    [HttpGet("review-queue")]
    [ProducesResponseType<PagedResult<ProjectSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectSummaryDto>>> GetReviewQueue(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new GetProjectReviewQueueQuery(search, page, pageSize);
        return Ok(await sender.Send(query, cancellationToken));
    }

    [HttpPost("{id}/submit")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> Submit(
        long id,
        [FromBody] SubmitProjectRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SubmitProjectCommand(id, request.ConcurrencyToken);
        return Ok(await sender.Send(command, cancellationToken));
    }

    [HttpPost("{id}/resubmit")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> Resubmit(
        long id,
        [FromBody] SubmitProjectRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ResubmitProjectCommand(id, request.ConcurrencyToken);
        return Ok(await sender.Send(command, cancellationToken));
    }

    [HttpPost("{id}/start-review")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> StartReview(
        long id,
        [FromBody] SubmitProjectRequest request,
        CancellationToken cancellationToken)
    {
        var command = new StartReviewProjectCommand(id, request.ConcurrencyToken);
        return Ok(await sender.Send(command, cancellationToken));
    }

    [HttpPost("{id}/revision")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> RequestRevision(
        long id,
        [FromBody] ProjectReviewRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RequestProjectRevisionCommand(id, request.ConcurrencyToken, request.Reason ?? string.Empty);
        return Ok(await sender.Send(command, cancellationToken));
    }

    [HttpPost("{id}/approve")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> Approve(
        long id,
        [FromBody] SubmitProjectRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ApproveProjectCommand(id, request.ConcurrencyToken);
        return Ok(await sender.Send(command, cancellationToken));
    }

    [HttpPost("{id}/reject")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDto>> Reject(
        long id,
        [FromBody] ProjectReviewRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RejectProjectCommand(id, request.ConcurrencyToken, request.Reason ?? string.Empty);
        return Ok(await sender.Send(command, cancellationToken));
    }

    [HttpGet("{id}/history")]
    [ProducesResponseType<IReadOnlyList<ProjectStatusHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProjectStatusHistoryDto>>> GetHistory(
        long id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetProjectStatusHistoryQuery(id), cancellationToken));
}
