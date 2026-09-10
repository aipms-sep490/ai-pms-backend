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
[Route("api/v1/academic/semesters")]
public sealed class SemestersController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SemesterDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SemesterDto>>> GetSemesters(
        [FromQuery] long? organizationId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetSemestersQuery(organizationId, search, status, page, pageSize),
            cancellationToken));

    [HttpGet("{semesterId:long}")]
    [ProducesResponseType<SemesterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SemesterDto>> GetSemester(
        long semesterId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSemesterByIdQuery(semesterId), cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    [ProducesResponseType<SemesterDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SemesterDto>> CreateSemester(
        CreateSemesterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateSemesterCommand(
                request.OrganizationId,
                request.Code,
                request.Name,
                request.StartDate,
                request.EndDate),
            cancellationToken);

        return CreatedAtAction(
            nameof(GetSemester),
            new { semesterId = result.Id },
            result);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{semesterId:long}")]
    [ProducesResponseType<SemesterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SemesterDto>> UpdateSemester(
        long semesterId,
        UpdateSemesterRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateSemesterCommand(
                semesterId,
                request.Code,
                request.Name,
                request.StartDate,
                request.EndDate),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPatch("{semesterId:long}/status")]
    [ProducesResponseType<SemesterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SemesterDto>> SetSemesterStatus(
        long semesterId,
        SetSemesterStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new SetSemesterStatusCommand(semesterId, request.Status, request.ExpectedStatus),
            cancellationToken));
}
