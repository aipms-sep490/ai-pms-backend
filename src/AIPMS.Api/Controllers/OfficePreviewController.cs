using AIPMS.Application.Features.Files;
using AIPMS.Application.Features.Files.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/files/office-preview")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OfficePreviewController(IOfficePreviewService preview) : ControllerBase
{
    [HttpPost, Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<OfficePreviewDto>> Read(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length is 0 or > 5 * 1024 * 1024)
            return BadRequest("Choose a DOCX or XLSX file up to 5 MiB for preview.");
        using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        return Ok(preview.Preview(file.FileName, content.ToArray()));
    }
}
