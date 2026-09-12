using AIPMS.Application.Features.WorkflowContext.DTOs;
using AIPMS.Application.Features.WorkflowContext.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkflowContextController(ISender sender) : ControllerBase
{
    [HttpGet("auth/me/context")]
    [ProducesResponseType<UserWorkflowContextDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserWorkflowContextDto>> Current([FromQuery] long? academicSemesterId, CancellationToken ct) =>
        Ok(await sender.Send(new GetUserWorkflowContextQuery(academicSemesterId), ct));

    [HttpGet("teams/{teamId:long}/actions")]
    [ProducesResponseType<TeamWorkflowActionsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TeamWorkflowActionsDto>> TeamActions(long teamId, CancellationToken ct) =>
        Ok(await sender.Send(new GetTeamWorkflowActionsQuery(teamId), ct));

    [HttpGet("projects/{projectId:long}/actions")]
    [ProducesResponseType<ProjectWorkflowActionsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectWorkflowActionsDto>> ProjectActions(long projectId, CancellationToken ct) =>
        Ok(await sender.Send(new GetProjectWorkflowActionsQuery(projectId), ct));
}
