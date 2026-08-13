using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Queries.GetProjectLifecycle;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(GetProjectLifecycleQuery lifecycleQuery) : ControllerBase
{
    [HttpGet("lifecycle")]
    [ProducesResponseType<ProjectLifecycleDto>(StatusCodes.Status200OK)]
    public ActionResult<ProjectLifecycleDto> GetLifecycle() => Ok(lifecycleQuery.Execute());
}
