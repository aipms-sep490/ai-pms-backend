using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.MilestoneTemplates.Commands;
using AIPMS.Application.Features.MilestoneTemplates.DTOs;
using AIPMS.Application.Features.MilestoneTemplates.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;
[ApiController, Authorize(Policy = AuthorizationPolicies.AdminOnly), Route("api/v1/milestone-templates")]
public sealed class MilestoneTemplatesController(ISender sender) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<MilestoneTemplateDto>> List(CancellationToken ct) => sender.Send(new GetMilestoneTemplatesQuery(), ct);
    [HttpPost] public Task<MilestoneTemplateDto> Create(SaveMilestoneTemplateRequest r, CancellationToken ct) => sender.Send(new CreateMilestoneTemplateCommand(r.Name, r.Description), ct);
    [HttpPost("{id:long}/versions")] public Task<MilestoneTemplateVersionDto> Version(long id, CancellationToken ct) => sender.Send(new CreateMilestoneTemplateVersionCommand(id), ct);
    [HttpPost("versions/{id:long}/publish")] public async Task<IActionResult> Publish(long id, CancellationToken ct) { await sender.Send(new PublishMilestoneTemplateVersionCommand(id), ct); return NoContent(); }
    [HttpPost("versions/{id:long}/items")] public Task<MilestoneTemplateVersionDto> AddItem(long id, SaveMilestoneTemplateItemRequest r, CancellationToken ct) => sender.Send(new AddMilestoneTemplateItemCommand(id, r), ct);
    [HttpPut("items/{id:long}")] public Task<MilestoneTemplateVersionDto> UpdateItem(long id, SaveMilestoneTemplateItemRequest r, CancellationToken ct) => sender.Send(new UpdateMilestoneTemplateItemCommand(id, r), ct);
    [HttpDelete("items/{id:long}")] public async Task<IActionResult> DeleteItem(long id, CancellationToken ct) { await sender.Send(new DeleteMilestoneTemplateItemCommand(id), ct); return NoContent(); }
    [HttpPost("periods/{periodId:long}/assign/{templateId:long}")] public async Task<IActionResult> Assign(long periodId, long templateId, CancellationToken ct) { await sender.Send(new AssignMilestoneTemplateCommand(periodId, templateId), ct); return NoContent(); }
}
