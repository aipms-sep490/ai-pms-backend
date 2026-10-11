using System.ComponentModel.DataAnnotations;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Cold.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class ColdSubmissionsController(IColdSubmissionService service) : ControllerBase
{
    [HttpGet("api/v1/cold-report-templates/examples/it")]
    public IReadOnlyList<ColdReportInput> ItExample() => ColdTemplateExamples.ItReports;
    [HttpGet("api/v1/cold-report-templates")]
    public Task<PagedResult<ColdTemplateDto>> Templates([FromQuery] long majorId, [FromQuery] long projectPeriodId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.Templates(majorId, projectPeriodId, page, pageSize, ct);
    [HttpPost("api/v1/cold-report-templates")]
    public async Task<ActionResult<ColdTemplateDto>> CreateTemplate(CreateColdTemplateRequest request, CancellationToken ct) =>
        StatusCode(201, await service.CreateTemplate(request, ct));
    [HttpPost("api/v1/projects/{projectId:long}/cold/checklist")]
    public async Task<ActionResult<ColdChecklistDto>> Apply(long projectId, ApplyColdChecklistRequest request, CancellationToken ct) =>
        StatusCode(201, await service.Apply(projectId, request, ct));
    [HttpGet("api/v1/projects/{projectId:long}/cold/checklist")]
    public Task<ColdChecklistDto> Checklist(long projectId, CancellationToken ct) => service.Checklist(projectId, ct);
    [HttpGet("api/v1/projects/{projectId:long}/cold/progress")]
    public Task<ColdProgressDto> Progress(long projectId, CancellationToken ct) => service.Progress(projectId, ct);
    [HttpPost("api/v1/projects/{projectId:long}/cold/lock")]
    public Task<ColdChecklistDto> Lock(long projectId, ColdTokenRequest request, CancellationToken ct) => service.Lock(projectId, request.ConcurrencyToken, ct);
    [HttpPost("api/v1/cold-checklist-items/{itemId:long}/draft")]
    public Task<ColdSubmissionDto> Draft(long itemId, SaveColdDraftRequest request, CancellationToken ct) => service.SaveDraft(itemId, request, ct);
    [HttpGet("api/v1/cold-checklist-items/{itemId:long}/history")]
    public Task<PagedResult<ColdSubmissionDto>> History(long itemId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => service.History(itemId, page, pageSize, ct);
    [HttpPost("api/v1/cold-submissions/{id:long}/files")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<ColdSubmissionDto> Upload(long id, [FromForm] ColdUploadForm request, CancellationToken ct)
    {
        await using var stream = request.File.OpenReadStream();
        return await service.Upload(id, request.ConcurrencyToken, new(request.File.FileName, request.File.ContentType, request.File.Length, stream), ct);
    }
    [HttpDelete("api/v1/cold-submissions/{id:long}/files/{fileId:long}")]
    public Task<ColdSubmissionDto> Remove(long id, long fileId, [FromQuery] string concurrencyToken, CancellationToken ct) => service.RemoveFile(id, fileId, concurrencyToken, ct);
    [HttpPost("api/v1/cold-submissions/{id:long}/submit")]
    public Task<ColdSubmissionDto> Submit(long id, ColdTokenRequest request, CancellationToken ct) => service.Submit(id, request.ConcurrencyToken, ct);
    [HttpGet("api/v1/cold-submissions/{id:long}/files/{fileId:long}/download")]
    public async Task<IActionResult> Download(long id, long fileId, CancellationToken ct)
    {
        var file = await service.Download(id, fileId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }
}
public sealed class ColdUploadForm
{
    [Required] public IFormFile File { get; init; } = null!;
    [Required] public string ConcurrencyToken { get; init; } = null!;
}
