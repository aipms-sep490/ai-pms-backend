using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/academic/hierarchy")]
public sealed class AcademicHierarchyController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AcademicHierarchyOrganizationDto>>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AcademicHierarchyOrganizationDto>>>
        GetHierarchy(
            [FromQuery] long? organizationId,
            [FromQuery] string? search,
            [FromQuery] bool includeInactive = false,
            CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetAcademicHierarchyQuery(organizationId, search, includeInactive),
            cancellationToken));
}
