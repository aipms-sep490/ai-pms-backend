using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.PeerEvaluations;
using AIPMS.Application.Features.PeerEvaluations.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize, Route("api/v1/projects/{projectId:long}/peer-evaluations")]
[ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403), ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409)]
public sealed class PeerEvaluationsController(IPeerEvaluationService service) : ControllerBase
{
    [HttpGet("rounds"), ProducesResponseType<PagedResult<PeerRoundDto>>(200)]
    public async Task<ActionResult<PagedResult<PeerRoundDto>>> Rounds(long projectId, int page = 1, int pageSize = 20,
        CancellationToken ct = default) => Ok(await service.Rounds(projectId, page, pageSize, ct));
    [HttpPost("rounds"), ProducesResponseType<PeerRoundDto>(200)]
    public async Task<ActionResult<PeerRoundDto>> Create(long projectId, CreatePeerRoundRequest input, CancellationToken ct) =>
        Ok(await service.Create(projectId, input, ct));
    [HttpPost("rounds/{roundId:long}/close"), ProducesResponseType<PeerRoundDto>(200)]
    public async Task<ActionResult<PeerRoundDto>> Close(long projectId, long roundId, PeerTokenRequest input, CancellationToken ct) =>
        Ok(await service.Close(projectId, roundId, input, ct));
    [HttpGet("mine"), ProducesResponseType<MyPeerEvaluationDto>(200)]
    public async Task<ActionResult<MyPeerEvaluationDto>> Mine(long projectId, long? roundId, CancellationToken ct) =>
        Ok(await service.Mine(projectId, roundId, ct));
    [HttpPut("rounds/{roundId:long}/mine"), ProducesResponseType<PeerEvaluationDto>(200)]
    public async Task<ActionResult<PeerEvaluationDto>> Save(long projectId, long roundId, SavePeerEvaluationRequest input, CancellationToken ct) =>
        Ok(await service.Save(projectId, roundId, input, ct));
    [HttpPost("rounds/{roundId:long}/mine/submit"), ProducesResponseType<PeerEvaluationDto>(200)]
    public async Task<ActionResult<PeerEvaluationDto>> Submit(long projectId, long roundId, PeerTokenRequest input, CancellationToken ct) =>
        Ok(await service.Submit(projectId, roundId, input, ct));
    [HttpGet, ProducesResponseType<PagedResult<PeerEvaluationDto>>(200)]
    public async Task<ActionResult<PagedResult<PeerEvaluationDto>>> Review(long projectId, [FromQuery] long roundId,
        int page = 1, int pageSize = 20, CancellationToken ct = default) => Ok(await service.Review(projectId, roundId, page, pageSize, ct));
}
