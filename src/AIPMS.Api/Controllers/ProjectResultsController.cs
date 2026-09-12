using AIPMS.Application.Features.Results.Commands;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Results.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:long}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class ProjectResultsController(ISender sender) : ControllerBase
{
    [HttpGet("result-policy")]
    [ProducesResponseType<ResultPolicyDto>(200)]
    [ProducesResponseType(204)]
    public async Task<ActionResult<ResultPolicyDto>> Policy(long projectId, CancellationToken ct)
    {
        var policy = await sender.Send(new GetResultPolicyQuery(projectId), ct);
        return policy is null ? NoContent() : Ok(policy);
    }
    [HttpPut("result-policy")]
    [ProducesResponseType<ResultPolicyDto>(200)]
    public async Task<ActionResult<ResultPolicyDto>> Configure(long projectId, ConfigureResultPolicyRequest input, CancellationToken ct) =>
        Ok(await sender.Send(new ConfigureResultPolicyCommand(projectId, input), ct));
    [HttpGet("result/preview")]
    [ProducesResponseType<ProjectResultPreviewDto>(200)]
    public async Task<ActionResult<ProjectResultPreviewDto>> Preview(long projectId, CancellationToken ct) =>
        Ok(await sender.Send(new PreviewProjectResultQuery(projectId), ct));
    [HttpPost("result")]
    [ProducesResponseType<ProjectResultDto>(201)]
    public async Task<ActionResult<ProjectResultDto>> Publish(long projectId, PublishProjectResultRequest input, CancellationToken ct) =>
        CreatedAtAction(nameof(Get), new { projectId }, await sender.Send(new PublishProjectResultCommand(projectId, input), ct));
    [HttpGet("result")]
    [ProducesResponseType<ProjectResultDto>(200)]
    public async Task<ActionResult<ProjectResultDto>> Get(long projectId, CancellationToken ct) =>
        Ok(await sender.Send(new GetProjectResultQuery(projectId), ct));
}
