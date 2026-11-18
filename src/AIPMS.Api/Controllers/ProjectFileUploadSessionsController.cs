using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Files;
using AIPMS.Application.Features.Files.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/files/upload-sessions")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(413)]
public sealed class ProjectFileUploadSessionsController(IProjectFileUploadSessions sessions,
    DeliverableWorkflow workflow, ICurrentUser user) : ControllerBase
{
    private long Actor => user.IsAuthenticated && user.UserId is long id ? id : throw new UnauthorizedException();

    [HttpPost]
    [ProducesResponseType<ProjectFileUploadSession>(201)]
    [RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<ProjectFileUploadSession>> Create(CreateProjectFileUploadSession request, CancellationToken ct)
    {
        await workflow.AuthorizeAttachmentAsync(request.ParentType, request.ParentId, ct);
        var session = await sessions.CreateAsync(Actor, request, ct);
        return CreatedAtAction(nameof(Get), new { sessionId = session.SessionId }, session);
    }

    [HttpGet("{sessionId}")]
    [ProducesResponseType<ProjectFileUploadSession>(200)]
    public async Task<ActionResult<ProjectFileUploadSession>> Get(string sessionId, CancellationToken ct)
    {
        var session = await sessions.GetAsync(Actor, sessionId, ct);
        await workflow.AuthorizeAttachmentReadAsync(session.ParentType, session.ParentId, ct);
        if (session.State == "COMPLETED" && session.File is not null)
            session = session with { File = await workflow.FileAsync(session.File.Id, ct) };
        return Ok(session);
    }

    [HttpPut("{sessionId}/chunks")]
    [ProducesResponseType<ProjectFileUploadSession>(200)]
    [Consumes("application/octet-stream")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<ActionResult<ProjectFileUploadSession>> Append(string sessionId, [FromQuery] long offset, CancellationToken ct)
    {
        var session = await sessions.GetAsync(Actor, sessionId, ct);
        await workflow.AuthorizeAttachmentAsync(session.ParentType, session.ParentId, ct);
        if (Request.ContentLength is not long length)
            throw new ValidationException(new Dictionary<string, string[]> { ["chunk"] = ["Phần tệp phải có kích thước xác định."] });
        return Ok(await sessions.AppendAsync(Actor, sessionId, offset, Request.Body, length, ct));
    }

    [HttpPost("{sessionId}/complete")]
    [ProducesResponseType<ProjectFileUploadSession>(202)]
    public async Task<ActionResult<ProjectFileUploadSession>> Complete(string sessionId, CancellationToken ct)
    {
        var session = await sessions.GetAsync(Actor, sessionId, ct);
        await workflow.AuthorizeAttachmentReadAsync(session.ParentType, session.ParentId, ct);
        if (session.State is "UPLOADING" or "FAILED")
            await workflow.AuthorizeAttachmentAsync(session.ParentType, session.ParentId, ct);
        var result = await sessions.CompleteAsync(Actor, sessionId, ct);
        if (result.State == "COMPLETED" && result.File is not null)
            result = result with { File = await workflow.FileAsync(result.File.Id, ct) };
        return Accepted(result);
    }

    [HttpDelete("{sessionId}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Cancel(string sessionId, CancellationToken ct)
    {
        await sessions.CancelAsync(Actor, sessionId, ct);
        return NoContent();
    }
}
