using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/supervisor-assignments")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class SupervisorAssignmentsController(ISender sender) : ControllerBase
{
    [HttpGet("{assignmentId:long}")]
    [ProducesResponseType<SupervisorAssignmentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupervisorAssignmentDto>> Get(long assignmentId, CancellationToken ct) =>
        Ok(await sender.Send(new GetSupervisorAssignmentQuery(assignmentId), ct));

    [HttpGet("/api/v1/projects/{projectId:long}/supervisor-assignments")]
    [ProducesResponseType<PagedResult<SupervisorAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupervisorAssignmentDto>>> ProjectAssignments(long projectId,
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetSupervisorAssignmentsQuery(projectId, status, page, pageSize), ct));

    [HttpGet("/api/v1/supervisors/assignments")]
    [ProducesResponseType<PagedResult<SupervisorAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupervisorAssignmentDto>>> OwnAssignments([FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetSupervisorAssignmentsQuery(null, status, page, pageSize), ct));

    [HttpPost("{assignmentId:long}/end")]
    [ProducesResponseType<SupervisorAssignmentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupervisorAssignmentDto>> End(long assignmentId, EndSupervisorAssignmentRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new EndSupervisorAssignmentCommand(assignmentId, request.Reason), ct));
}
