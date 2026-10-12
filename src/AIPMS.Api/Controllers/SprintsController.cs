using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Sprints;
using AIPMS.Application.Features.Sprints.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize, Route("api/v1/projects/{projectId:long}/sprints")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(401), ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409)]
public sealed class SprintsController(ISprintService service) : ControllerBase
{
    [HttpGet, ProducesResponseType<PagedResult<SprintDto>>(200)]
    public Task<PagedResult<SprintDto>> List(long projectId, CancellationToken ct, int page = 1, int pageSize = 20) => service.List(projectId, page, pageSize, ct);
    [HttpPost, ProducesResponseType<SprintDto>(200)]
    public Task<SprintDto> Create(long projectId, SaveSprintRequest input, CancellationToken ct) => service.Save(projectId, null, input, ct);
    [HttpPut("{id:long}"), ProducesResponseType<SprintDto>(200)]
    public Task<SprintDto> Update(long projectId, long id, SaveSprintRequest input, CancellationToken ct) => service.Save(projectId, id, input, ct);
    [HttpPost("{id:long}/start"), ProducesResponseType<SprintDto>(200)]
    public Task<SprintDto> Start(long projectId, long id, SprintTokenRequest input, CancellationToken ct) => service.Start(projectId, id, input, ct);
    [HttpPost("{id:long}/complete"), ProducesResponseType<SprintDto>(200)]
    public Task<SprintDto> Complete(long projectId, long id, CompleteSprintRequest input, CancellationToken ct) => service.Complete(projectId, id, input, ct);
    [HttpGet("{id:long}/burndown"), ProducesResponseType<SprintBurndownDto>(200)]
    public Task<SprintBurndownDto> Burndown(long projectId, long id, CancellationToken ct) => service.Burndown(projectId, id, ct);
    [HttpPut("/api/v1/tasks/{taskId:long}/board"), ProducesResponseType<TaskDto>(200)]
    public Task<TaskDto> SaveTask(long taskId, SaveTaskBoardRequest input, CancellationToken ct) => service.SaveTask(taskId, input, ct);
    [HttpGet("/api/v1/projects/{projectId:long}/task-board"), ProducesResponseType<PagedResult<TaskDto>>(200)]
    public Task<PagedResult<TaskDto>> Board(long projectId, CancellationToken ct, long? sprintId = null, bool backlog = false,
        string? label = null, long? majorId = null, string? status = null, int page = 1, int pageSize = 20) =>
        service.Board(projectId, sprintId, backlog, label, majorId, status, page, pageSize, ct);
}
