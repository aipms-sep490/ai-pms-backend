using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Queries.GetSupervisorById;
using AIPMS.Application.Features.Supervisors.Queries.GetSupervisors;
using AIPMS.Application.Features.Supervisors.Commands.UpdateSupervisorProfile;
using AIPMS.Application.Features.Supervisors.Commands.UpdateSupervisorExpertise;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/supervisors")]
public sealed class SupervisorsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SupervisorDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupervisorDto>>> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] bool? isAvailable = null,
        [FromQuery] string? expertise = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new GetSupervisorsQuery(pageNumber, pageSize, search, isAvailable, expertise),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType<SupervisorDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupervisorDetailDto>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSupervisorByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPut("me/profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> UpdateProfile(
        [FromBody] UpdateSupervisorProfileCommand command,
        CancellationToken cancellationToken)
    {
        await sender.Send(command, cancellationToken);
        return Ok();
    }

    [HttpPut("me/expertise")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> UpdateExpertise(
        [FromBody] UpdateSupervisorExpertiseCommand command,
        CancellationToken cancellationToken)
    {
        await sender.Send(command, cancellationToken);
        return Ok();
    }
}
