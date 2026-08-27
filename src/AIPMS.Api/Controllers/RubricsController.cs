using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize, Route("api/v1/rubrics")]
public sealed class RubricsController(ISender sender) : ControllerBase
{
    [HttpPost] public async Task<ActionResult<RubricDto>> Create(CreateRubricPayload p, CancellationToken ct) => Ok(await sender.Send(new CreateRubricCommand(p.DepartmentId, p.AcademicSemesterId, p.Code, p.Name, p.Description, p.VersionNumber), ct));
    [HttpGet] public async Task<ActionResult<PagedResult<RubricDto>>> List([FromQuery] bool? active, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await sender.Send(new ListRubricsQuery(active, pageNumber, pageSize), ct));
    [HttpGet("{id:long}")] public async Task<ActionResult<RubricDto>> Get(long id, CancellationToken ct) => Ok(await sender.Send(new GetRubricQuery(id), ct));
    [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id, UpdateRubricPayload p, CancellationToken ct) { await sender.Send(new UpdateRubricCommand(id, p.Name, p.Description), ct); return NoContent(); }
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id, CancellationToken ct) { await sender.Send(new DeleteRubricCommand(id), ct); return NoContent(); }
    [HttpPut("{id:long}/criteria/{criterionId:long}")] public async Task<IActionResult> Criterion(long id, long criterionId, UpsertRubricCriterionPayload p, CancellationToken ct) { await sender.Send(new UpsertRubricCriterionCommand(id, criterionId, p.WeightPercent, p.MaxScore, p.SortOrder, p.IsRequired), ct); return NoContent(); }
    [HttpDelete("{id:long}/criteria/{rubricCriterionId:long}")] public async Task<IActionResult> DeleteCriterion(long id, long rubricCriterionId, CancellationToken ct) { await sender.Send(new DeleteRubricCriterionCommand(id, rubricCriterionId), ct); return NoContent(); }
    [HttpPut("{id:long}/criteria/order")] public async Task<IActionResult> Reorder(long id, ReorderRubricCriteriaPayload p, CancellationToken ct) { await sender.Send(new ReorderRubricCriteriaCommand(id, p.OrderedIds), ct); return NoContent(); }
    [HttpPut("{id:long}/active")] public async Task<IActionResult> Active(long id, SetRubricActivePayload p, CancellationToken ct) { await sender.Send(new SetRubricActiveCommand(id, p.Active), ct); return NoContent(); }
}

public sealed record CreateRubricPayload(long? DepartmentId, long? AcademicSemesterId, string Code, string Name, string? Description, int VersionNumber);
public sealed record UpdateRubricPayload(string Name, string? Description);
public sealed record UpsertRubricCriterionPayload(decimal WeightPercent, decimal MaxScore, int SortOrder, bool IsRequired);
public sealed record ReorderRubricCriteriaPayload(IReadOnlyList<long> OrderedIds);
public sealed record SetRubricActivePayload(bool Active);

