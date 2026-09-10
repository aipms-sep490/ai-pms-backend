using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Semesters.Commands;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Semesters.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/academic/project-periods")]
public sealed class ProjectPeriodsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ProjectPeriodDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectPeriodDto>>> GetProjectPeriods(
        [FromQuery] long? semesterId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? periodType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetProjectPeriodsQuery(semesterId, search, status, periodType, page, pageSize),
            cancellationToken));

    [HttpGet("{periodId:long}")]
    [ProducesResponseType<ProjectPeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectPeriodDto>> GetProjectPeriod(
        long periodId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetProjectPeriodByIdQuery(periodId), cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    [ProducesResponseType<ProjectPeriodDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectPeriodDto>> CreateProjectPeriod(
        CreateProjectPeriodRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateProjectPeriodCommand(
                request.AcademicSemesterId,
                request.Code,
                request.Name,
                request.PeriodType,
                request.StartAt,
                request.EndAt,
                request.MinTeamSize,
                request.MaxTeamSize,
                request.MinDistinctMajors,
                request.MaxProjectsPerSupervisor,
                request.MilestoneTemplateId,
                request.RubricId),
            cancellationToken);

        return CreatedAtAction(
            nameof(GetProjectPeriod),
            new { periodId = result.Id },
            result);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{periodId:long}")]
    [ProducesResponseType<ProjectPeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectPeriodDto>> UpdateProjectPeriod(
        long periodId,
        UpdateProjectPeriodRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateProjectPeriodCommand(
                periodId,
                request.Code,
                request.Name,
                request.PeriodType,
                request.StartAt,
                request.EndAt,
                request.MinTeamSize,
                request.MaxTeamSize,
                request.MinDistinctMajors,
                request.MaxProjectsPerSupervisor,
                request.MilestoneTemplateId,
                request.RubricId),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPatch("{periodId:long}/status")]
    [ProducesResponseType<ProjectPeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectPeriodDto>> SetProjectPeriodStatus(
        long periodId,
        SetProjectPeriodStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new SetProjectPeriodStatusCommand(periodId, request.Status, request.ExpectedStatus),
            cancellationToken));
}
