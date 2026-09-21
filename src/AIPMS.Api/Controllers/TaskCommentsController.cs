using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.TaskComments.Commands;
using AIPMS.Application.Features.TaskComments.DTOs;
using AIPMS.Application.Features.TaskComments.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AIPMS.Api.Controllers;
[ApiController, Authorize, Route("api/v1/tasks/{taskId:long}/comments")]
public sealed class TaskCommentsController(ISender sender) : ControllerBase
{
    [HttpGet] public Task<PagedResult<TaskCommentDto>> List(long taskId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => sender.Send(new GetTaskCommentsQuery(taskId, page, pageSize), ct);
    [HttpPost] public Task<TaskCommentDto> Create(long taskId, CreateTaskCommentRequest request, CancellationToken ct) => sender.Send(new CreateTaskCommentCommand(taskId, request.Content), ct);
}
