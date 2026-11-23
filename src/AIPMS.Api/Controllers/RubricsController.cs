using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rubrics")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class RubricsController(ISender sender, IRubricWorkbookService workbooks) : ControllerBase
{
    [HttpGet("{id:long}/workbook")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> DownloadWorkbook(long id, CancellationToken ct) => File(
        await workbooks.ExportAsync(id, ct),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"rubric-{id}.xlsx");

    [HttpPost("{id:long}/workbook/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    [ProducesResponseType<RubricWorkbookPreview>(200)]
    public async Task<RubricWorkbookPreview> PreviewWorkbook(long id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length is 0 or > 5 * 1024 * 1024
            || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A valid XLSX workbook up to 5 MiB is required.");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return await workbooks.PreviewAsync(id, buffer.ToArray(), ct);
    }
    [HttpGet]
    [ProducesResponseType<PagedResult<RubricDto>>(200)]
    public async Task<ActionResult<PagedResult<RubricDto>>> List([FromQuery] long? departmentId = null,
        [FromQuery] long? academicSemesterId = null, [FromQuery] string? status = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] long? majorId = null, [FromQuery] string? projectMode = null,
        [FromQuery] string? evaluationStage = null, [FromQuery] string? evaluationScope = null,
        CancellationToken ct = default) => Ok(await sender.Send(
            new GetRubricsQuery(departmentId, academicSemesterId, status, search, page, pageSize,
                majorId, projectMode, evaluationStage, evaluationScope), ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<RubricDto>(200)]
    public async Task<ActionResult<RubricDto>> Get(long id, CancellationToken ct) =>
        Ok(await sender.Send(new GetRubricQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<RubricDto>(201)]
    public async Task<ActionResult<RubricDto>> Create(CreateRubricRequest request, CancellationToken ct)
    {
        var rubric = await sender.Send(new CreateRubricCommand(request), ct);
        return CreatedAtAction(nameof(Get), new { id = rubric.Id }, rubric);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<RubricDto>(200)]
    public async Task<ActionResult<RubricDto>> Update(long id, UpdateRubricRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new UpdateRubricCommand(id, request), ct));

    [HttpPost("{id:long}/submit-for-approval")]
    [ProducesResponseType<RubricDto>(200)]
    public async Task<ActionResult<RubricDto>> SubmitForApproval(long id, SubmitRubricForApprovalRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new SubmitRubricForApprovalCommand(id, request), ct));

    [HttpPost("{id:long}/approve")]
    [ProducesResponseType<RubricDto>(200)]
    public async Task<ActionResult<RubricDto>> Approve(long id, ApproveRubricRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new ApproveRubricCommand(id, request), ct));

    [HttpPost("{id:long}/reject")]
    [ProducesResponseType<RubricDto>(200)]
    public async Task<ActionResult<RubricDto>> Reject(long id, RejectRubricRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new RejectRubricCommand(id, request), ct));

    [HttpPost("{id:long}/publish")]
    [ProducesResponseType<RubricDto>(200)]
    public async Task<ActionResult<RubricDto>> Publish(long id, ChangeRubricStatusRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new PublishRubricCommand(id, request.ConcurrencyToken), ct));

    [HttpPost("{id:long}/retire")]
    [ProducesResponseType<RubricDto>(200)]
    public async Task<ActionResult<RubricDto>> Retire(long id, ChangeRubricStatusRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new RetireRubricCommand(id, request.ConcurrencyToken), ct));

    [HttpPost("{id:long}/versions")]
    [ProducesResponseType<RubricDto>(201)]
    public async Task<ActionResult<RubricDto>> NewVersion(long id, CreateRubricVersionRequest request, CancellationToken ct)
    {
        var rubric = await sender.Send(new CreateRubricVersionCommand(id, request), ct);
        return CreatedAtAction(nameof(Get), new { id = rubric.Id }, rubric);
    }

    [HttpPost("{id:long}/criteria/{criterionId:long}/clone")]
    [ProducesResponseType<RubricDto>(200)]
    public async Task<ActionResult<RubricDto>> CloneCriterion(long id, long criterionId, CloneRubricCriterionRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new CloneRubricCriterionCommand(id, criterionId, request.ConcurrencyToken), ct));

    [HttpDelete("{id:long}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(long id, [FromQuery] string concurrencyToken, CancellationToken ct)
    {
        await sender.Send(new DeleteRubricCommand(id, concurrencyToken), ct);
        return NoContent();
    }
}
