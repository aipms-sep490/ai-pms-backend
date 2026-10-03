using AIPMS.Application.Features.ExecutionCapabilities.Abstractions;
using AIPMS.Application.Features.ExecutionCapabilities.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class ExecutionCapabilitiesController(IExecutionCapabilityService service) : ControllerBase
{
    [HttpGet("projects/{projectId:long}/execution-actions")]
    [ProducesResponseType<ExecutionCapabilityDto>(StatusCodes.Status200OK)]
    public Task<ExecutionCapabilityDto> Project(long projectId, CancellationToken ct) => service.GetProjectAsync(projectId, ct);

    [HttpGet("tasks/{taskId:long}/execution-actions")]
    [ProducesResponseType<ExecutionCapabilityDto>(StatusCodes.Status200OK)]
    public Task<ExecutionCapabilityDto> Task(long taskId, CancellationToken ct) => service.GetTaskAsync(taskId, ct);

    [HttpGet("milestones/{milestoneId:long}/execution-actions")]
    [ProducesResponseType<ExecutionCapabilityDto>(StatusCodes.Status200OK)]
    public Task<ExecutionCapabilityDto> Milestone(long milestoneId, CancellationToken ct) => service.GetMilestoneAsync(milestoneId, ct);
}
