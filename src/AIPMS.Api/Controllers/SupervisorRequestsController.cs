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
[Route("api/v1/supervisor-requests")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class SupervisorRequestsController(ISender sender) : ControllerBase
{
    [HttpPost("/api/v1/projects/{projectId:long}/supervisor-requests")]
    [ProducesResponseType<SupervisorRequestDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupervisorRequestDto>> Send(long projectId, SendSupervisorRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new SendSupervisorRequestCommand(projectId, request.SupervisorProfileId, request.Message), ct));

    [HttpGet("/api/v1/projects/{projectId:long}/supervisor-requests")]
    [ProducesResponseType<PagedResult<SupervisorRequestDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupervisorRequestDto>>> ProjectRequests(long projectId,
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetSupervisorRequestsQuery(projectId, status, page, pageSize), ct));

    [HttpGet("/api/v1/supervisors/requests")]
    [ProducesResponseType<PagedResult<SupervisorRequestDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupervisorRequestDto>>> Inbox([FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetSupervisorRequestsQuery(null, status, page, pageSize), ct));

    [HttpPost("{requestId:long}/cancel")]
    [ProducesResponseType<SupervisorRequestDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupervisorRequestDto>> Cancel(long requestId, CancellationToken ct) =>
        Ok(await sender.Send(new CancelSupervisorRequestCommand(requestId), ct));

    [HttpPost("{requestId:long}/accept")]
    [ProducesResponseType<SupervisorRequestDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupervisorRequestDto>> Accept(long requestId, RespondToSupervisorRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new AcceptSupervisorRequestCommand(requestId, request.Message), ct));

    [HttpPost("{requestId:long}/reject")]
    [ProducesResponseType<SupervisorRequestDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SupervisorRequestDto>> Reject(long requestId, RespondToSupervisorRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new RejectSupervisorRequestCommand(requestId, request.Message), ct));
}
