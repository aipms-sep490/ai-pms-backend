using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/supervisors")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class SupervisorsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<SupervisorProfileDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<SupervisorProfileDto>>> List(
        [FromQuery] long? departmentId, [FromQuery] string? search, [FromQuery] string? expertise,
        [FromQuery] bool? isAvailable, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetSupervisorsQuery(departmentId, search, expertise, isAvailable, page, pageSize), cancellationToken));

    [HttpGet("{profileId:long}")]
    [ProducesResponseType<SupervisorProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupervisorProfileDto>> Get(long profileId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSupervisorByIdQuery(profileId), cancellationToken));

    // User ID is explicit: first update provisions the lecturer's unique profile.
    [HttpPut("users/{userId:long}/profile")]
    [ProducesResponseType<SupervisorProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupervisorProfileDto>> UpdateProfile(long userId,
        UpdateSupervisorProfileRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new UpdateSupervisorProfileCommand(userId, request.Bio, request.IsAvailable), cancellationToken));

    [HttpPut("{profileId:long}/expertise")]
    [ProducesResponseType<SupervisorProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupervisorProfileDto>> ReplaceExpertise(long profileId,
        ReplaceSupervisorExpertiseRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new ReplaceSupervisorExpertiseCommand(profileId, request.Expertise), cancellationToken));
}
