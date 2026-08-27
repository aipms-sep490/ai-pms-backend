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
[Route("api/v1/projects/{projectId:long}")]
public sealed class ProjectSupervisorsController(ISender sender):ControllerBase
{
    [HttpPost("supervisor-requests")]
    public async Task<ActionResult<SupervisorRequestDto>> Send(long projectId,[FromBody] SendSupervisorRequestPayload payload,CancellationToken ct)
        =>Ok(await sender.Send(new SendSupervisorRequestCommand(projectId,payload.SupervisorId,payload.RequestMessage),ct));

    [HttpGet("supervisor-candidates")]
    public async Task<ActionResult<IReadOnlyList<SupervisorCandidateDto>>> Candidates(long projectId,[FromQuery]string? expertise,CancellationToken ct)
        =>Ok(await sender.Send(new GetSupervisorCandidatesQuery(projectId,expertise),ct));

    [HttpGet("supervisor")]
    public async Task<ActionResult<SupervisorDto>> Get(long projectId,CancellationToken ct)
        =>Ok(await sender.Send(new GetProjectSupervisorQuery(projectId),ct));
}

public sealed record SendSupervisorRequestPayload(long SupervisorId,string? RequestMessage);
