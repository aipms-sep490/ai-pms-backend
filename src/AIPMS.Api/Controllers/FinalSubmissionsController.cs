using AIPMS.Application.Features.FinalSubmissions.Commands;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:long}/final-submission")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class FinalSubmissionsController(ISender sender) : ControllerBase
{
    [HttpGet("requirements")]
    [ProducesResponseType<FinalRequirementsDto>(200)]
    public async Task<ActionResult<FinalRequirementsDto>> Requirements(long projectId, CancellationToken ct) =>
        Ok(await sender.Send(new GetFinalRequirementsQuery(projectId), ct));

    [HttpPut("requirements")]
    [ProducesResponseType<FinalRequirementsDto>(200)]
    public async Task<ActionResult<FinalRequirementsDto>> Configure(long projectId, ConfigureFinalRequirementsRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new ConfigureFinalRequirementsCommand(projectId, request), ct));

    [HttpGet("checklist")]
    [ProducesResponseType<FinalSubmissionChecklistDto>(200)]
    public async Task<ActionResult<FinalSubmissionChecklistDto>> Checklist(long projectId, CancellationToken ct) =>
        Ok(await sender.Send(new GetFinalSubmissionChecklistQuery(projectId), ct));

    [HttpPost]
    [ProducesResponseType<FinalSubmissionDto>(201)]
    public async Task<ActionResult<FinalSubmissionDto>> Submit(long projectId, SubmitFinalSubmissionRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new SubmitFinalSubmissionCommand(projectId, request), ct);
        return CreatedAtAction(nameof(Get), new { projectId }, result);
    }

    [HttpGet]
    [ProducesResponseType<FinalSubmissionDto>(200)]
    public async Task<ActionResult<FinalSubmissionDto>> Get(long projectId, CancellationToken ct) =>
        Ok(await sender.Send(new GetFinalSubmissionQuery(projectId), ct));

    [HttpGet("files/{fileId:long}/download")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    public async Task<IActionResult> Download(long projectId, long fileId, CancellationToken ct)
    {
        var file = await sender.Send(new DownloadFinalSubmissionFileQuery(projectId, fileId), ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }
}
