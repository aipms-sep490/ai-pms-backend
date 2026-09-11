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
[Route("api/v1/deliverables")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class DeliverablesController(ISender sender) : ControllerBase
{
    [HttpGet("/api/v1/projects/{projectId:long}/deliverables")]
    [ProducesResponseType<PagedResult<DeliverableDto>>(200)]
    public async Task<ActionResult<PagedResult<DeliverableDto>>> List(long projectId, [FromQuery] string? status,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] long? milestoneId = null, [FromQuery] string? deliverableType = null, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetDeliverablesQuery(projectId, status, search, page, pageSize, milestoneId, deliverableType), ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<DeliverableDto>(200)]
    public async Task<ActionResult<DeliverableDto>> Get(long id, CancellationToken ct) => Ok(await sender.Send(new GetDeliverableQuery(id), ct));

    [HttpPost("/api/v1/projects/{projectId:long}/deliverables")]
    [ProducesResponseType<DeliverableDto>(201)]
    public async Task<ActionResult<DeliverableDto>> Create(long projectId, SaveDeliverableRequest request, CancellationToken ct)
    {
        var item = await sender.Send(new CreateDeliverableCommand(projectId, request), ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<DeliverableDto>(200)]
    public async Task<ActionResult<DeliverableDto>> Update(long id, SaveDeliverableRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new UpdateDeliverableCommand(id, request), ct));

    [HttpDelete("{id:long}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await sender.Send(new DeleteDeliverableCommand(id), ct);
        return NoContent();
    }

    [HttpGet("{id:long}/versions")]
    [ProducesResponseType<PagedResult<DeliverableVersionDto>>(200)]
    public async Task<ActionResult<PagedResult<DeliverableVersionDto>>> Versions(long id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetDeliverableVersionsQuery(id, page, pageSize), ct));

    [HttpGet("/api/v1/deliverable-versions/{id:long}")]
    [ProducesResponseType<DeliverableVersionDto>(200)]
    public async Task<ActionResult<DeliverableVersionDto>> Version(long id, CancellationToken ct) =>
        Ok(await sender.Send(new GetDeliverableVersionQuery(id), ct));

    [HttpPost("{id:long}/versions")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(22 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]
    [ProducesResponseType<DeliverableVersionDto>(201)]
    public async Task<ActionResult<DeliverableVersionDto>> Submit(long id, [FromForm] SubmitDeliverableForm request, CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        var result = await sender.Send(new SubmitDeliverableVersionCommand(id, request.ExpectedLatestVersion, request.Note,
            new(request.File.FileName, request.File.ContentType, request.File.Length, content)), ct);
        return CreatedAtAction(nameof(Version), new { id = result.Id }, result);
    }

    [HttpPost("/api/v1/deliverable-versions/{id:long}/review")]
    [ProducesResponseType<DeliverableFeedbackDto>(200)]
    public async Task<ActionResult<DeliverableFeedbackDto>> Review(long id, ReviewDeliverableRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new ReviewDeliverableVersionCommand(id, request.Decision, request.Feedback), ct));

    [HttpGet("/api/v1/deliverable-versions/{id:long}/feedback")]
    [ProducesResponseType<PagedResult<DeliverableFeedbackDto>>(200)]
    public async Task<ActionResult<PagedResult<DeliverableFeedbackDto>>> Feedback(long id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetDeliverableFeedbackQuery(id, page, pageSize), ct));
}

public sealed class SubmitDeliverableForm
{
    [Required] public IFormFile File { get; init; } = null!;
    [Required] public int? ExpectedLatestVersion { get; init; }
    public string? Note { get; init; }
}
