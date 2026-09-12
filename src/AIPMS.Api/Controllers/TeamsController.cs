using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Commands;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/teams")]
public sealed class TeamsController(ISender sender) : ControllerBase
{
    [HttpGet("current")]
    public async Task<ActionResult<TeamDto>> Current([FromQuery] long academicSemesterId, CancellationToken ct)
    {
        var team = await sender.Send(new GetCurrentTeamQuery(academicSemesterId), ct);
        return team is null ? NoContent() : Ok(team);
    }

    [HttpGet("{teamId:long}")]
    public async Task<ActionResult<TeamDto>> Get(long teamId, CancellationToken ct) =>
        Ok(await sender.Send(new GetTeamQuery(teamId), ct));

    [HttpPost]
    public async Task<ActionResult<TeamDto>> Create(CreateTeamRequest request, CancellationToken ct)
    {
        var team = await sender.Send(new CreateTeamCommand(
            request.AcademicSemesterId, request.Code, request.Name, request.Description, request.AcademicScope), ct);
        return CreatedAtAction(nameof(Get), new { teamId = team.Id }, team);
    }

    [HttpPut("{teamId:long}")]
    public async Task<ActionResult<TeamDto>> Update(long teamId, UpdateTeamRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new UpdateTeamCommand(teamId, request.Name, request.Description), ct));

    [HttpPut("{teamId:long}/academic-scope")]
    public async Task<ActionResult<TeamDto>> SetAcademicScope(long teamId, TeamAcademicScopeRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new SetTeamAcademicScopeCommand(teamId, request), ct));

    [HttpPost("{teamId:long}/eligibility/refresh")]
    public async Task<ActionResult<TeamDto>> RefreshEligibility(long teamId, CancellationToken ct) =>
        Ok(await sender.Send(new RefreshTeamEligibilityCommand(teamId), ct));

    [HttpGet("{teamId:long}/invitation-candidates")]
    [ProducesResponseType<PagedResult<TeamInvitationCandidateDto>>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<PagedResult<TeamInvitationCandidateDto>>> InvitationCandidates(long teamId,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await sender.Send(new GetTeamInvitationCandidatesQuery(teamId, search, page, pageSize), ct));

    [HttpPost("{teamId:long}/invitations")]
    public async Task<ActionResult<TeamInvitationDto>> Invite(long teamId, InviteTeamMemberRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new InviteTeamMemberCommand(teamId, request.InvitedUserId, request.Message), ct));

    [HttpGet("invitations")]
    public async Task<ActionResult<PagedResult<TeamInvitationDto>>> Invitations(
        [FromQuery] long? teamId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetTeamInvitationsQuery(teamId, page, pageSize), ct));

    [HttpPost("invitations/{invitationId:long}/accept")]
    public async Task<ActionResult<TeamDto>> Accept(long invitationId, CancellationToken ct) =>
        Ok(await sender.Send(new AcceptTeamInvitationCommand(invitationId), ct));

    [HttpPost("invitations/{invitationId:long}/reject")]
    public async Task<IActionResult> Reject(long invitationId, CancellationToken ct)
    {
        await sender.Send(new RejectTeamInvitationCommand(invitationId), ct);
        return NoContent();
    }

    [HttpPost("invitations/{invitationId:long}/cancel")]
    public async Task<IActionResult> Cancel(long invitationId, CancellationToken ct)
    {
        await sender.Send(new CancelTeamInvitationCommand(invitationId), ct);
        return NoContent();
    }

    [HttpDelete("{teamId:long}/members/{userId:long}")]
    public async Task<IActionResult> RemoveMember(long teamId, long userId, CancellationToken ct)
    {
        await sender.Send(new RemoveTeamMemberCommand(teamId, userId), ct);
        return NoContent();
    }

    [HttpPost("{teamId:long}/leave")]
    public async Task<IActionResult> Leave(long teamId, CancellationToken ct)
    {
        await sender.Send(new RemoveTeamMemberCommand(teamId, null), ct);
        return NoContent();
    }

    [HttpPost("{teamId:long}/leader")]
    public async Task<ActionResult<TeamDto>> TransferLeader(long teamId, TransferTeamLeaderRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new TransferTeamLeaderCommand(teamId, request.NewLeaderUserId), ct));
}

public sealed record CreateTeamRequest(long AcademicSemesterId, string Code, string Name, string? Description,
    TeamAcademicScopeRequest? AcademicScope = null);
public sealed record UpdateTeamRequest(string Name, string? Description);
public sealed record InviteTeamMemberRequest(long InvitedUserId, string? Message);
public sealed record TransferTeamLeaderRequest(long NewLeaderUserId);
