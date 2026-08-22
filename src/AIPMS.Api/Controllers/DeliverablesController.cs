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
[Route("api/v1/deliverables")]
public sealed class DeliverablesController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<DeliverableDto>> Create([FromBody] CreateDeliverableCommand command, CancellationToken ct) => Ok(await sender.Send(command,ct));
    [HttpGet("{id:long}")]
    public async Task<ActionResult<DeliverableDto>> Get(long id,CancellationToken ct)=>Ok(await sender.Send(new GetDeliverableQuery(id),ct));
    [HttpGet]
    public async Task<ActionResult<PagedResult<DeliverableDto>>> List([FromQuery] long projectId,[FromQuery] int pageNumber=1,[FromQuery] int pageSize=20,CancellationToken ct=default)=>Ok(await sender.Send(new GetDeliverablesQuery(projectId,pageNumber,pageSize),ct));
    [HttpPut("{id:long}")]
    public async Task<ActionResult> Update(long id,[FromBody] UpdateDeliverablePayload body,CancellationToken ct){await sender.Send(new UpdateDeliverableCommand(id,body.Title,body.Description,body.DeliverableType,body.DueAt),ct);return Ok();}
    [HttpDelete("{id:long}")]
    public async Task<ActionResult> Delete(long id,CancellationToken ct){await sender.Send(new DeleteDeliverableCommand(id),ct);return NoContent();}
    [HttpPost("{id:long}/versions")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<DeliverableVersionDto>> Submit(long id,[FromForm] SubmitDeliverableVersionPayload body,CancellationToken ct){await using var stream=body.File.OpenReadStream();return Ok(await sender.Send(new SubmitDeliverableVersionCommand(id,body.Note,body.File.FileName,body.File.ContentType,body.File.Length,stream),ct));}
    [HttpGet("{id:long}/versions")]
    public async Task<ActionResult<IReadOnlyList<DeliverableVersionDto>>> History(long id,CancellationToken ct)=>Ok(await sender.Send(new GetDeliverableHistoryQuery(id),ct));
    [HttpPost("versions/{id:long}/feedback")]
    public async Task<ActionResult> Feedback(long id,[FromBody] FeedbackPayload body,CancellationToken ct){await sender.Send(new AddSupervisorFeedbackCommand(id,body.FeedbackText),ct);return Ok();}
}

[ApiController]
[Authorize]
[Route("api/v1/files")]
public sealed class DeliverableFilesController(ISender sender) : ControllerBase
{
    [HttpGet("{id:long}/download")]
    public async Task<IActionResult> Download(long id,CancellationToken ct){var file=await sender.Send(new DownloadDeliverableFileQuery(id),ct);return File(file.Content,file.MimeType??"application/octet-stream",file.OriginalFileName);}
    [HttpDelete("{id:long}")]
    public async Task<ActionResult> Delete(long id,CancellationToken ct){await sender.Send(new DeleteDeliverableFileCommand(id),ct);return NoContent();}
}
public sealed record UpdateDeliverablePayload(string Title,string? Description,string? DeliverableType,DateTime? DueAt);
public sealed record FeedbackPayload(string FeedbackText);
public sealed class SubmitDeliverableVersionPayload { public string? Note { get; init; } = null; public IFormFile File { get; init; } = null!; }
