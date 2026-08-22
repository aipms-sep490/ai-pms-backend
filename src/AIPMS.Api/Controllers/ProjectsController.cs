using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Queries.GetProjectLifecycle;
using AIPMS.Application.Features.Supervisors.Commands.SendSupervisorRequest;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Queries.GetProjectSupervisor;
using AIPMS.Application.Features.Supervisors.Queries.GetSupervisorCandidates;
using MediatR;
using Microsoft.AspNetCore.Authorization;
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

    [HttpPost("{projectId:long}/supervisor-requests")]
    public async Task<ActionResult<SupervisorRequestDto>> SendSupervisorRequest(
        long projectId,
        [FromBody] SendSupervisorRequestPayload payload,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new SendSupervisorRequestCommand(projectId, payload.SupervisorId, payload.RequestMessage), cancellationToken));

    [HttpGet("{projectId:long}/supervisor")]
    public async Task<ActionResult<SupervisorDto>> GetSupervisor(long projectId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProjectSupervisorQuery(projectId), cancellationToken);
        return result is null ? NoContent() : Ok(result);
    }

    [HttpGet("{projectId:long}/supervisor-candidates")]
    public async Task<ActionResult<IReadOnlyList<SupervisorCandidateDto>>> GetSupervisorCandidates(
        long projectId,
        [FromQuery] string? expertise,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSupervisorCandidatesQuery(projectId, expertise), cancellationToken));
}

public sealed record SendSupervisorRequestPayload(long SupervisorId, string? RequestMessage);
