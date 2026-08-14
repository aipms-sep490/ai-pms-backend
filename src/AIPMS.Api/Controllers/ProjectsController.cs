using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Queries.GetProjectLifecycle;
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
}
