using System.ComponentModel.DataAnnotations;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.Commands;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/files")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class FilesController(ISender sender) : ControllerBase
{
    [HttpGet("/api/v1/projects/{projectId:long}/files")]
    [ProducesResponseType<PagedResult<ProjectFileDto>>(200)]
    public async Task<ActionResult<PagedResult<ProjectFileDto>>> List(long projectId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? contentType = null, [FromQuery] long? uploadedBy = null,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string? parentType = null, [FromQuery] long? parentId = null, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetProjectFilesQuery(projectId, search, page, pageSize,
            contentType, uploadedBy, from, to, parentType, parentId), ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<ProjectFileDto>(200)]
    public async Task<ActionResult<ProjectFileDto>> Get(long id, CancellationToken ct) =>
        Ok(await sender.Send(new GetProjectFileQuery(id), ct));

    [HttpGet("{id:long}/download")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var file = await sender.Send(new DownloadProjectFileQuery(id), ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(22 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]
    [ProducesResponseType<ProjectFileDto>(201)]
    public async Task<ActionResult<ProjectFileDto>> Upload([FromForm] UploadProjectFileForm request, CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        var file = await sender.Send(new UploadProjectFileCommand(request.ParentType, request.ParentId,
            new(request.File.FileName, request.File.ContentType, request.File.Length, content)), ct);
        return CreatedAtAction(nameof(Get), new { id = file.Id }, file);
    }

    [HttpDelete("{id:long}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await sender.Send(new DeleteProjectFileCommand(id), ct);
        return NoContent();
    }
}

public sealed class UploadProjectFileForm
{
    [Required] public IFormFile File { get; init; } = null!;
    [Required] public string ParentType { get; init; } = null!;
    public long ParentId { get; init; }
}
