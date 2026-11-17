using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.DepartmentDocuments;
using AIPMS.Application.Features.DepartmentDocuments.Abstractions;
using AIPMS.Application.Features.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/academic/department-documents")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DepartmentDocumentsController(IDepartmentDocumentService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DepartmentDocumentDto>>> List(
        [FromQuery] long majorId, [FromQuery] long? projectPeriodId, CancellationToken ct) =>
        Ok(await service.List(majorId, projectPeriodId, ct));

    [HttpPost, Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadPolicies.MultipartTransportCeiling)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadPolicies.MultipartTransportCeiling)]
    public async Task<ActionResult<DepartmentDocumentDto>> Upload([FromForm] DepartmentDocumentUploadForm form, CancellationToken ct)
    {
        await using var content = form.File.OpenReadStream();
        var row = await service.Upload(form.MajorId, form.ProjectPeriodId, form.Code, form.Title,
            new UploadContent(form.File.FileName, form.File.ContentType, form.File.Length, content), ct);
        return Created($"/api/v1/academic/department-documents/{row.Id}/download", row);
    }

    [HttpPost("{id:long}/publish")]
    public async Task<ActionResult<DepartmentDocumentDto>> Publish(long id, ChangeDepartmentDocumentStatusRequest request, CancellationToken ct) =>
        Ok(await service.Publish(id, request.ConcurrencyToken, ct));

    [HttpPost("{id:long}/retire")]
    public async Task<ActionResult<DepartmentDocumentDto>> Retire(long id, ChangeDepartmentDocumentStatusRequest request, CancellationToken ct) =>
        Ok(await service.Retire(id, request.ConcurrencyToken, ct));

    [HttpGet("{id:long}/download")]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var file = await service.Download(id, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }
}

public sealed class DepartmentDocumentUploadForm
{
    public long MajorId { get; init; }
    public long? ProjectPeriodId { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public IFormFile File { get; init; } = null!;
}
