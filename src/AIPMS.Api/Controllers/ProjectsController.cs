using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Queries.GetProjectLifecycle;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(ISender sender) : ControllerBase
{
    [HttpGet("lifecycle")]
    [ProducesResponseType<ProjectLifecycleDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectLifecycleDto>> GetLifecycle(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetProjectLifecycleQuery(), cancellationToken));
}
