using System.ComponentModel.DataAnnotations;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Appeals;
using AIPMS.Application.Features.Appeals.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/projects/{projectId:long}/result")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class ResultAppealsController(IAppealService appeals) : ControllerBase
{
    [HttpGet("appeal-policy")]
    public Task<AppealPolicyDto?> Policy(long projectId, CancellationToken ct) => appeals.Policy(projectId, ct);
    /// <summary>Lead staff records an existing academic approval source as a new immutable policy version. No default/demo approval.</summary>
    [HttpPost("appeal-policy")]
    public Task<AppealPolicyDto> RecordPolicy(long projectId, RecordAppealPolicyRequest input, CancellationToken ct) => appeals.RecordPolicy(projectId, input, ct);
    [HttpGet("appeal-windows")]
    public Task<PagedResult<AppealWindowDto>> Windows(long projectId, CancellationToken ct, int page = 1, int pageSize = 20) => appeals.Windows(projectId, page, pageSize, ct);
    [HttpPost("appeal-windows")]
    public Task<AppealWindowDto> Open(long projectId, OpenAppealWindowRequest input, CancellationToken ct) => appeals.OpenWindow(projectId, input, ct);
    [HttpPost("appeal-windows/{id:long}/close")]
    public Task<AppealWindowDto> Close(long projectId, long id, AppealTokenRequest input, CancellationToken ct) => appeals.CloseWindow(projectId, id, input.ConcurrencyToken, ct);
    [HttpGet("correction-requests")]
    public Task<PagedResult<CorrectionRequestDto>> List(long projectId, CancellationToken ct, int page = 1, int pageSize = 20) => appeals.List(projectId, page, pageSize, ct);
    [HttpPost("correction-requests")]
    public Task<CorrectionRequestDto> Create(long projectId, CreateCorrectionRequest input, CancellationToken ct) => appeals.Create(projectId, input, ct);
    [HttpGet("correction-requests/{id:long}")]
    public Task<CorrectionRequestDto> Get(long projectId, long id, CancellationToken ct) => appeals.Get(projectId, id, ct);
    [HttpPost("correction-requests/{id:long}/reviewer")]
    public Task<CorrectionRequestDto> Assign(long projectId, long id, AssignAppealReviewerRequest input, CancellationToken ct) => appeals.Assign(projectId, id, input, ct);
    [HttpPut("correction-requests/{id:long}/review")]
    public Task<CorrectionRequestDto> Review(long projectId, long id, ReviewAppealRequest input, CancellationToken ct) => appeals.Review(projectId, id, input, ct);
    [HttpGet("correction-requests/{id:long}/review")]
    public Task<AppealReviewContextDto> ReviewContext(long projectId, long id, CancellationToken ct) => appeals.ReviewContext(projectId, id, ct);
    [HttpGet("correction-requests/{id:long}/preview")]
    public Task<AppealPreviewDto> Preview(long projectId, long id, CancellationToken ct) => appeals.Preview(projectId, id, ct);
    [HttpPost("correction-requests/{id:long}/decision")]
    public Task<CorrectionRequestDto> Decide(long projectId, long id, AppealDecisionRequest input, CancellationToken ct) => appeals.Decide(projectId, id, input, ct);
    [HttpGet("correction-requests/{id:long}/history")]
    public Task<PagedResult<AppealEventDto>> History(long projectId, long id, CancellationToken ct, int page = 1, int pageSize = 20) => appeals.History(projectId, id, page, pageSize, ct);
    [HttpGet("correction-requests/{id:long}/evidence")]
    public Task<IReadOnlyList<AppealFileDto>> Evidence(long projectId, long id, CancellationToken ct) => appeals.Evidence(projectId, id, ct);
    [HttpGet("correction-requests/{id:long}/evidence/{fileId:long}")]
    public async Task<IActionResult> EvidenceFile(long projectId, long id, long fileId, CancellationToken ct)
    {
        var file = await appeals.DownloadEvidence(projectId, id, fileId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }
    [HttpGet("correction-requests/{id:long}/files/{fileId:long}")]
    public async Task<IActionResult> Download(long projectId, long id, long fileId, CancellationToken ct)
    {
        var file = await appeals.Download(projectId, id, fileId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }
    [HttpPost("correction-requests/{id:long}/files")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<CorrectionRequestDto> Upload(long projectId, long id, [FromForm] AppealUploadForm input, CancellationToken ct)
    {
        await using var stream = input.File.OpenReadStream();
        return await appeals.Upload(projectId, id, input.ConcurrencyToken, new(input.File.FileName, input.File.ContentType, input.File.Length, stream), ct);
    }
}
public sealed class AppealUploadForm
{
    [Required] public IFormFile File { get; init; } = null!;
    [Required] public string ConcurrencyToken { get; init; } = null!;
}
