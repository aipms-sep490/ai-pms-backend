using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.ProjectLabels;
using AIPMS.Application.Features.ProjectLabels.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ProjectLabelsController(IProjectLabelService service) : ControllerBase
{
    [HttpGet("api/v1/projects/{projectId:long}/labels")]
    public async Task<ActionResult<IReadOnlyList<ProjectLabelDto>>> List(long projectId, CancellationToken ct) =>
        Ok(await service.ListAsync(projectId, ct));

    [HttpPost("api/v1/projects/{projectId:long}/labels")]
    public async Task<ActionResult<ProjectLabelDto>> Create(long projectId, [FromBody] SaveProjectLabelRequest request, CancellationToken ct) =>
        Ok(await service.CreateAsync(projectId, request, ct));

    [HttpPut("api/v1/projects/{projectId:long}/labels/{labelId:long}")]
    public async Task<ActionResult<ProjectLabelDto>> Update(long projectId, long labelId, [FromBody] SaveProjectLabelRequest request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(projectId, labelId, request, ct));

    [HttpDelete("api/v1/projects/{projectId:long}/labels/{labelId:long}")]
    public async Task<IActionResult> Delete(long projectId, long labelId, CancellationToken ct)
    {
        await service.DeleteAsync(projectId, labelId, ct);
        return NoContent();
    }

    [HttpPatch("api/v1/tasks/{taskId:long}/taxonomy")]
    public async Task<ActionResult<TaskTaxonomyStateDto>> UpdateTaskTaxonomy(long taskId, [FromBody] UpdateTaskTaxonomyRequest request, CancellationToken ct) =>
        Ok(await service.UpdateTaskTaxonomyAsync(taskId, request, ct));
}
