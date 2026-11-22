using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/projects/{projectId:long}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(422)]
public sealed class IndividualProcessScoresController(IIndividualProcessScoringService service) : ControllerBase
{
    [HttpGet("individual-process-scores")]
    [ProducesResponseType<ProjectIndividualProcessScoresDto>(200)]
    public Task<ProjectIndividualProcessScoresDto> GetProjectScores(long projectId, CancellationToken ct) =>
        service.GetProjectScoresAsync(projectId, ct);

    [HttpPut("gates/{gateInstanceId:long}/individual-process-scores/{studentId:long}")]
    [ProducesResponseType<GateIndividualProcessScoreItemDto>(200)]
    public Task<GateIndividualProcessScoreItemDto> SaveDraft(
        long projectId, long gateInstanceId, long studentId, UpsertIndividualProcessScoreRequest input, CancellationToken ct) =>
        service.SaveDraftAsync(projectId, gateInstanceId, studentId, input, ct);

    [HttpPost("gates/{gateInstanceId:long}/individual-process-scores/{studentId:long}/finalize")]
    [ProducesResponseType<GateIndividualProcessScoreItemDto>(200)]
    public Task<GateIndividualProcessScoreItemDto> FinalizeScore(
        long projectId, long gateInstanceId, long studentId, FinalizeIndividualProcessScoreRequest input, CancellationToken ct) =>
        service.FinalizeAsync(projectId, gateInstanceId, studentId, input, ct);

    [HttpPut("gates/{gateInstanceId:long}/individual-process-scores/{studentId:long}/mentor-note")]
    [ProducesResponseType<GateIndividualProcessScoreItemDto>(200)]
    public Task<GateIndividualProcessScoreItemDto> SaveMentorReferenceNote(
        long projectId, long gateInstanceId, long studentId, UpsertMentorReferenceNoteRequest input, CancellationToken ct) =>
        service.SaveMentorReferenceNoteAsync(projectId, gateInstanceId, studentId, input, ct);
}
