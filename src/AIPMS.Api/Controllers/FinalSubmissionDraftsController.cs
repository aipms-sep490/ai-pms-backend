using AIPMS.Application.Features.FinalSubmissions.Commands;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:long}/final-submission-draft")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class FinalSubmissionDraftsController(ISender sender) : ControllerBase
{
    [HttpGet("/api/v1/projects/{projectId:long}/final-submission-periods")]
    [ProducesResponseType<PagedResult<FinalSubmissionPeriodOptionDto>>(200)]
    public async Task<ActionResult<PagedResult<FinalSubmissionPeriodOptionDto>>> Periods(long projectId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetFinalSubmissionPeriodsQuery(projectId, page, pageSize), ct));

    [HttpGet]
    [ProducesResponseType<FinalSubmissionDraftDto>(200)]
    public async Task<ActionResult<FinalSubmissionDraftDto>> Get(long projectId, CancellationToken ct) =>
        Ok(await sender.Send(new GetFinalSubmissionDraftQuery(projectId), ct));

    [HttpPost]
    [ProducesResponseType<FinalSubmissionDraftDto>(201)]
    public async Task<ActionResult<FinalSubmissionDraftDto>> Create(long projectId, CreateFinalSubmissionDraftRequest request, CancellationToken ct)
    {
        var draft = await sender.Send(new CreateFinalSubmissionDraftCommand(projectId, request), ct);
        return CreatedAtAction(nameof(Get), new { projectId }, draft);
    }

    [HttpPut]
    [ProducesResponseType<FinalSubmissionDraftDto>(200)]
    public async Task<ActionResult<FinalSubmissionDraftDto>> Update(long projectId, UpdateFinalSubmissionDraftRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new UpdateFinalSubmissionDraftCommand(projectId, request), ct));
}
