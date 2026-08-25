using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Commands;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/academic/majors")]
public sealed class MajorsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<MajorDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MajorDto>>> GetMajors(
        [FromQuery] long? organizationId,
        [FromQuery] long? departmentId,
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetMajorsQuery(
                organizationId,
                departmentId,
                search,
                isActive,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("{majorId:long}")]
    [ProducesResponseType<MajorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MajorDto>> GetMajor(
        long majorId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMajorByIdQuery(majorId), cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpPost]
    [ProducesResponseType<MajorDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MajorDto>> CreateMajor(
        CreateMajorRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateMajorCommand(
                request.DepartmentId,
                request.Code,
                request.Name,
                request.Description),
            cancellationToken);

        return CreatedAtAction(
            nameof(GetMajor),
            new { majorId = result.Id },
            result);
    }

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpPut("{majorId:long}")]
    [ProducesResponseType<MajorDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MajorDto>> UpdateMajor(
        long majorId,
        UpdateMajorRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateMajorCommand(
                majorId,
                request.DepartmentId,
                request.Code,
                request.Name,
                request.Description),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpPatch("{majorId:long}/status")]
    [ProducesResponseType<MajorDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MajorDto>> SetMajorStatus(
        long majorId,
        SetAcademicRecordStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new SetMajorStatusCommand(majorId, request.IsActive),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpDelete("{majorId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeactivateMajor(
        long majorId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new SetMajorStatusCommand(majorId, false), cancellationToken);
        return NoContent();
    }
}
