using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Commands;
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
public sealed class RubricsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<RubricDto>>(200)]
    public async Task<ActionResult<PagedResult<RubricDto>>> List([FromQuery] long? departmentId = null,
        [FromQuery] long? academicSemesterId = null, [FromQuery] string? status = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await sender.Send(
            new GetRubricsQuery(departmentId, academicSemesterId, status, search, page, pageSize), ct));

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

    [HttpDelete("{id:long}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(long id, [FromQuery] string concurrencyToken, CancellationToken ct)
    {
        await sender.Send(new DeleteRubricCommand(id, concurrencyToken), ct);
        return NoContent();
    }
}
