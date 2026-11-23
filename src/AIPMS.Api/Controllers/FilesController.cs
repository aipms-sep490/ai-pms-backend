using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Files;
using AIPMS.Application.Features.Files.Abstractions;
using System.ComponentModel.DataAnnotations;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.Commands;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Queries;
using AIPMS.Infrastructure.Persistence.Generated;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/files")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class FilesController(
    ISender sender,
    AipmsDbContext db,
    ICurrentUser currentUser,
    ISignedPreviewTokenService previewTokens,
    IOfficePreviewService officePreview,
    TimeProvider clock) : ControllerBase
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
        var scanStatus = await db.Files.AsNoTracking().Where(f => f.Id == id).Select(f => f.ScanStatus).SingleOrDefaultAsync(ct);
        if (scanStatus is not null && scanStatus != FileScanStatuses.Ready)
            throw new ConflictException("File is quarantined or not ready for download.", "FILE_QUARANTINED");
        var file = await sender.Send(new DownloadProjectFileQuery(id), ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        if (file.Length.HasValue) Response.ContentLength = file.Length.Value;
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("{id:long}/preview-token")]
    [ProducesResponseType<SignedPreviewTokenDto>(200)]
    public async Task<ActionResult<SignedPreviewTokenDto>> IssuePreviewToken(long id, CancellationToken ct)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException();
        var meta = await sender.Send(new GetProjectFileQuery(id), ct);
        var scanStatus = await db.Files.AsNoTracking().Where(f => f.Id == id).Select(f => f.ScanStatus).SingleOrDefaultAsync(ct) ?? FileScanStatuses.Ready;
        if (scanStatus != FileScanStatuses.Ready)
            throw new ConflictException("File is quarantined or failed preview and cannot be streamed.", "FILE_QUARANTINED");
        var expires = clock.GetUtcNow().UtcDateTime.AddMinutes(5);
        var token = previewTokens.Issue(new SignedPreviewClaims("FILE", id, actorId, null, expires));
        return Ok(new SignedPreviewTokenDto(
            token, expires,
            $"/api/v1/files/{id}/stream?token={Uri.EscapeDataString(token)}",
            $"/api/v1/files/{id}/safe-preview",
            meta.ContentType, meta.FileName, scanStatus));
    }

    [HttpGet("{id:long}/stream")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    public async Task<IActionResult> Stream(long id, [FromQuery] string token, CancellationToken ct)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException();
        if (previewTokens.Validate(token, "FILE", id, actorId) is null)
            throw new ForbiddenException("Preview stream token is invalid, expired, or bound to another user.", WorkflowErrorCodes.Forbidden);
        var scanStatus = await db.Files.AsNoTracking().Where(f => f.Id == id).Select(f => f.ScanStatus).SingleOrDefaultAsync(ct);
        if (scanStatus is not null && scanStatus != FileScanStatuses.Ready)
            throw new ConflictException("File is quarantined or not ready for streaming.", "FILE_QUARANTINED");
        var file = await sender.Send(new DownloadProjectFileQuery(id), ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentDisposition = $"inline; filename=\"{Path.GetFileName(file.FileName).Replace("\"", "")}\"";
        return File(file.Content, file.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{id:long}/safe-preview")]
    [ProducesResponseType<OfficePreviewDto>(200)]
    public async Task<ActionResult<OfficePreviewDto>> SafePreview(long id, CancellationToken ct)
    {
        var scanStatus = await db.Files.AsNoTracking().Where(f => f.Id == id).Select(f => f.ScanStatus).SingleOrDefaultAsync(ct);
        if (scanStatus is not null && scanStatus != FileScanStatuses.Ready)
            throw new ConflictException("Quarantined or unready files cannot be previewed.", "FILE_QUARANTINED");
        var file = await sender.Send(new DownloadProjectFileQuery(id), ct);
        await using (file.Content)
        {
            using var ms = new MemoryStream();
            await file.Content.CopyToAsync(ms, ct);
            return Ok(officePreview.Preview(file.FileName, ms.ToArray()));
        }
    }

    [HttpGet("/api/v1/tasks/{taskId:long}/evidence")]
    [ProducesResponseType<PagedResult<ProjectFileDto>>(200)]
    public Task<PagedResult<ProjectFileDto>> TaskEvidence(long taskId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => sender.Send(new GetTaskEvidenceQuery(taskId, page, pageSize), ct);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadPolicies.MultipartTransportCeiling)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadPolicies.MultipartTransportCeiling)]
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
