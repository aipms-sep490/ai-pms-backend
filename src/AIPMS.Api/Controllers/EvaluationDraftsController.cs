using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class EvaluationDraftsController(ISender sender) : ControllerBase
{
    [HttpPost("api/v1/projects/{projectId:long}/evaluation-assignments")]
    [ProducesResponseType<EvaluationAssignmentDto>(201)]
    public async Task<ActionResult<EvaluationAssignmentDto>> Assign(long projectId, AssignEvaluatorRequest request, CancellationToken ct) =>
        StatusCode(201, await sender.Send(new AssignEvaluatorCommand(projectId, request), ct));

    [HttpGet("api/v1/projects/{projectId:long}/evaluation-assignments")]
    [ProducesResponseType<PagedResult<EvaluationAssignmentDto>>(200)]
    public async Task<ActionResult<PagedResult<EvaluationAssignmentDto>>> Assignments(long projectId,
        [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetEvaluationAssignmentsQuery(projectId, status, page, pageSize), ct));

    [HttpGet("api/v1/evaluation-assignments/my")]
    [ProducesResponseType<PagedResult<EvaluationAssignmentDto>>(200)]
    public async Task<ActionResult<PagedResult<EvaluationAssignmentDto>>> MyAssignments(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetEvaluationAssignmentsQuery(null, "ACTIVE", page, pageSize), ct));

    [HttpPost("api/v1/evaluation-assignments/{id:long}/revoke")]
    [ProducesResponseType<EvaluationAssignmentDto>(200)]
    public async Task<ActionResult<EvaluationAssignmentDto>> Revoke(long id, RevokeEvaluatorRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new RevokeEvaluatorCommand(id, request), ct));

    [HttpPost("api/v1/evaluation-assignments/{id:long}/evaluation")]
    [ProducesResponseType<EvaluationDraftDto>(201)]
    public async Task<ActionResult<EvaluationDraftDto>> Create(long id, CancellationToken ct)
    {
        var draft = await sender.Send(new CreateEvaluationDraftCommand(id), ct);
        return CreatedAtAction(nameof(Get), new { id = draft.Id }, draft);
    }

    [HttpGet("api/v1/evaluations/{id:long}")]
    [ProducesResponseType<EvaluationDraftDto>(200)]
    public async Task<ActionResult<EvaluationDraftDto>> Get(long id, CancellationToken ct) =>
        Ok(await sender.Send(new GetEvaluationDraftQuery(id), ct));

    [HttpPut("api/v1/evaluations/{id:long}/draft")]
    [ProducesResponseType<EvaluationDraftDto>(200)]
    public async Task<ActionResult<EvaluationDraftDto>> Save(long id, SaveEvaluationDraftRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new SaveEvaluationDraftCommand(id, request), ct));

    [HttpGet("api/v1/projects/{projectId:long}/evaluations")]
    [ProducesResponseType<PagedResult<EvaluationDraftDto>>(200)]
    public async Task<ActionResult<PagedResult<EvaluationDraftDto>>> List(long projectId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetProjectEvaluationsQuery(projectId, page, pageSize), ct));

    [HttpPost("api/v1/evaluations/{id:long}/finalize")]
    [ProducesResponseType<EvaluationDraftDto>(200)]
    public async Task<ActionResult<EvaluationDraftDto>> FinalizeEvaluation(long id, FinalizeEvaluationRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new FinalizeEvaluationCommand(id, request), ct));
}
