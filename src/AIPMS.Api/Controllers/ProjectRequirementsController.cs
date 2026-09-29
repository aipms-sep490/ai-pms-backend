using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:long}")]
public sealed class ProjectRequirementsController(IProjectRequirementsService service) : ControllerBase
{
    [HttpGet("major-requirements")]
    public async Task<ActionResult<ProjectRequirementsDto>> Get(long projectId, CancellationToken ct) => Ok(await service.GetAsync(projectId, ct));

    [HttpPut("major-requirements")]
    public async Task<ActionResult<ProjectRequirementsDto>> Replace(long projectId, ReplaceProjectRequirementsRequest request, CancellationToken ct) =>
        Ok(await service.ReplaceAsync(projectId, request, ct));

    [HttpGet("review-snapshots")]
    public async Task<ActionResult<ProjectReviewHistoryDto>> History(long projectId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await service.GetHistoryAsync(projectId, page, pageSize, ct));
}
