using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Disciplines.Abstractions;
using AIPMS.Application.Features.Disciplines.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
public sealed class DisciplineGovernanceController(IDisciplineService service) : ControllerBase
{
    [HttpGet("api/v1/teams/{teamId:long}/major-requirements/{majorId:long}/responsibilities")]
    public async Task<ActionResult<ResponsibilityListDto>> TeamResponsibilities(long teamId, long majorId, CancellationToken ct) =>
        Ok(await service.TeamResponsibilitiesAsync(teamId, majorId, ct));
    [HttpPut("api/v1/teams/{teamId:long}/major-requirements/{majorId:long}/responsibilities")]
    public async Task<ActionResult<ResponsibilityListDto>> ReplaceResponsibilities(long teamId, long majorId, ReplaceResponsibilitiesRequest request, CancellationToken ct) =>
        Ok(await service.ReplaceResponsibilitiesAsync(teamId, majorId, request, ct));
    [HttpGet("api/v1/projects/{projectId:long}/major-requirements/{majorId:long}/responsibilities")]
    public async Task<ActionResult<ResponsibilityListDto>> ProjectResponsibilities(long projectId, long majorId, CancellationToken ct) =>
        Ok(await service.ProjectResponsibilitiesAsync(projectId, majorId, ct));
    [HttpGet("api/v1/tasks/{taskId:long}/disciplines")]
    public async Task<ActionResult<TaskDisciplinesDto>> TaskDisciplines(long taskId, CancellationToken ct) => Ok(await service.TaskDisciplinesAsync(taskId, ct));
    [HttpPut("api/v1/tasks/{taskId:long}/disciplines")]
    public async Task<ActionResult<TaskDisciplinesDto>> ReplaceTaskDisciplines(long taskId, ReplaceTaskDisciplinesRequest request, CancellationToken ct) =>
        Ok(await service.ReplaceTaskDisciplinesAsync(taskId, request, ct));
    [HttpGet("api/v1/projects/{projectId:long}/evidence")]
    public async Task<ActionResult<PagedResult<ProjectEvidenceDto>>> Evidence(long projectId, [FromQuery] string? sourceType,
        [FromQuery] long? majorId, [FromQuery] string? verificationStatus, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await service.EvidenceAsync(projectId, sourceType, majorId, verificationStatus, page, pageSize, ct));
    [HttpPost("api/v1/projects/{projectId:long}/evidence")]
    public async Task<ActionResult<ProjectEvidenceDto>> AddEvidence(long projectId, CreateProjectEvidenceRequest request, CancellationToken ct) =>
        Ok(await service.AddEvidenceAsync(projectId, request, ct));
}
