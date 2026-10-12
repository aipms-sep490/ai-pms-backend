using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize, Route("api/v1/projects/{projectId:long}/supervisor-recommendations")]
[ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403), ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409)]
public sealed class SupervisorRecommendationsController(ISupervisorRecommendationService service) : ControllerBase
{
    [HttpGet, ProducesResponseType<SupervisorRecommendationPreview>(200)]
    public async Task<ActionResult<SupervisorRecommendationPreview>> Preview(long projectId,
        [FromQuery] SupervisorRecommendationRequest query, CancellationToken ct) => Ok(await service.Preview(projectId, query, ct));
    [HttpPost, ProducesResponseType<SupervisorRecommendationRun>(200)]
    public async Task<ActionResult<SupervisorRecommendationRun>> Create(long projectId,
        SupervisorRecommendationRequest request, CancellationToken ct) => Ok(await service.Create(projectId, request, ct));
    [HttpGet("history"), ProducesResponseType<PagedResult<SupervisorRecommendationRunSummary>>(200)]
    public async Task<ActionResult<PagedResult<SupervisorRecommendationRunSummary>>> History(long projectId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await service.History(projectId, page, pageSize, ct));
    [HttpGet("history/{runId:long}"), ProducesResponseType<SupervisorRecommendationRun>(200)]
    public async Task<ActionResult<SupervisorRecommendationRun>> Get(long projectId, long runId, CancellationToken ct) => Ok(await service.Get(projectId, runId, ct));
}
