using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize, Route("api/v1")]
public sealed class EvaluationsController(ISender sender) : ControllerBase
{
    [HttpPost("projects/{projectId:long}/evaluations")]
    public async Task<ActionResult<EvaluationDetailDto>> Create(long projectId,
        CreateEvaluationPayload payload, CancellationToken ct) => Ok(await sender.Send(
            new CreateEvaluationDraftCommand(projectId, payload.RubricId,
                payload.EvaluationType, payload.EvidenceSummary), ct));

    [HttpGet("projects/{projectId:long}/evaluations")]
    public async Task<ActionResult<PagedResult<EvaluationDto>>> List(long projectId,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await sender.Send(
            new GetProjectEvaluationsQuery(projectId, pageNumber, pageSize), ct));

    [HttpGet("evaluations/{id:long}")]
    public async Task<ActionResult<EvaluationDetailDto>> Get(long id, CancellationToken ct) =>
        Ok(await sender.Send(new GetEvaluationQuery(id), ct));

    [HttpPut("evaluations/{id:long}/criteria/{rubricCriterionId:long}/score")]
    public async Task<IActionResult> Score(long id, long rubricCriterionId,
        ScorePayload payload, CancellationToken ct)
    { await sender.Send(new UpdateEvaluationScoreCommand(id, rubricCriterionId, payload.Score, payload.Comments), ct); return NoContent(); }

    [HttpPut("evaluations/{id:long}/comment")]
    public async Task<IActionResult> Comment(long id, CommentPayload payload, CancellationToken ct)
    { await sender.Send(new UpdateEvaluationCommentCommand(id, payload.Comments, payload.EvidenceSummary), ct); return NoContent(); }

    [HttpPost("evaluations/{id:long}/finalize")]
    public async Task<IActionResult> Finalize(long id, CancellationToken ct)
    { await sender.Send(new FinalizeEvaluationCommand(id), ct); return NoContent(); }
}

public sealed record CreateEvaluationPayload(long RubricId, string EvaluationType, string? EvidenceSummary);
public sealed record ScorePayload(decimal Score, string? Comments);
public sealed record CommentPayload(string? Comments, string? EvidenceSummary);

