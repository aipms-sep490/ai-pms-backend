using AIPMS.Application.Features.Supervisors.Commands.EndSupervisorAssignment;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/supervisor-assignments")]
public sealed class SupervisorAssignmentsController(ISender sender) : ControllerBase
{
    [HttpPost("{id:long}/end")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> EndAssignment(
        long id,
        CancellationToken cancellationToken)
    {
        await sender.Send(new EndSupervisorAssignmentCommand(id), cancellationToken);
        return Ok();
    }
}
