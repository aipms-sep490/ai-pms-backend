using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Commands;
using AIPMS.Application.Features.Teams.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/team-leader-change-requests")]
public sealed class TeamLeaderChangeRequestsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TeamLeaderChangeRequestDto>>> List(
        [FromQuery] long? teamId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetTeamLeaderChangeRequestsQuery(teamId, status, page, pageSize), ct));

    [HttpPost("{requestId:long}/approve")]
    public async Task<ActionResult<TeamLeaderChangeRequestDto>> Approve(long requestId,
        RespondToTeamLeaderChange request, CancellationToken ct) =>
        Ok(await sender.Send(new ApproveTeamLeaderChangeCommand(requestId, request.Message), ct));

    [HttpPost("{requestId:long}/reject")]
    public async Task<ActionResult<TeamLeaderChangeRequestDto>> Reject(long requestId,
        RespondToTeamLeaderChange request, CancellationToken ct) =>
        Ok(await sender.Send(new RejectTeamLeaderChangeCommand(requestId, request.Message), ct));
}
