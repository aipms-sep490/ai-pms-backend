using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
public sealed class ProjectGovernanceController(IProjectGovernanceService service) : ControllerBase
{
    [HttpGet("api/v1/projects/{projectId:long}/governance")]
    [ProducesResponseType<ProjectGovernanceDto>(StatusCodes.Status200OK)]
    public Task<ProjectGovernanceDto> Get(long projectId, CancellationToken ct) => service.GetAsync(projectId, ct);
}
