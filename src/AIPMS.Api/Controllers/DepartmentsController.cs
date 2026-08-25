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
[Route("api/v1/academic/departments")]
public sealed class DepartmentsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<DepartmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DepartmentDto>>> GetDepartments(
        [FromQuery] long? organizationId,
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetDepartmentsQuery(organizationId, search, isActive, page, pageSize),
            cancellationToken));

    [HttpGet("{departmentId:long}")]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentDto>> GetDepartment(
        long departmentId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new GetDepartmentByIdQuery(departmentId),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DepartmentDto>> CreateDepartment(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateDepartmentCommand(
                request.OrganizationId,
                request.Code,
                request.Name,
                request.Description),
            cancellationToken);

        return CreatedAtAction(
            nameof(GetDepartment),
            new { departmentId = result.Id },
            result);
    }

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpPut("{departmentId:long}")]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DepartmentDto>> UpdateDepartment(
        long departmentId,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateDepartmentCommand(
                departmentId,
                request.Code,
                request.Name,
                request.Description),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpPatch("{departmentId:long}/status")]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DepartmentDto>> SetDepartmentStatus(
        long departmentId,
        SetAcademicRecordStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new SetDepartmentStatusCommand(departmentId, request.IsActive),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpDelete("{departmentId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeactivateDepartment(
        long departmentId,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new SetDepartmentStatusCommand(departmentId, false),
            cancellationToken);
        return NoContent();
    }
}
