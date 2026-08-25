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
[Route("api/v1/academic/organizations")]
public sealed class OrganizationsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<OrganizationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrganizationDto>>> GetOrganizations(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetOrganizationsQuery(search, isActive, page, pageSize),
            cancellationToken));

    [HttpGet("{organizationId:long}")]
    [ProducesResponseType<OrganizationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDto>> GetOrganization(
        long organizationId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new GetOrganizationByIdQuery(organizationId),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    [ProducesResponseType<OrganizationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationDto>> CreateOrganization(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateOrganizationCommand(request.Code, request.Name, request.Description),
            cancellationToken);

        return CreatedAtAction(
            nameof(GetOrganization),
            new { organizationId = result.Id },
            result);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{organizationId:long}")]
    [ProducesResponseType<OrganizationDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationDto>> UpdateOrganization(
        long organizationId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateOrganizationCommand(
                organizationId,
                request.Code,
                request.Name,
                request.Description),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPatch("{organizationId:long}/status")]
    [ProducesResponseType<OrganizationDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationDto>> SetOrganizationStatus(
        long organizationId,
        SetAcademicRecordStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new SetOrganizationStatusCommand(organizationId, request.IsActive),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{organizationId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeactivateOrganization(
        long organizationId,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new SetOrganizationStatusCommand(organizationId, false),
            cancellationToken);
        return NoContent();
    }
}
