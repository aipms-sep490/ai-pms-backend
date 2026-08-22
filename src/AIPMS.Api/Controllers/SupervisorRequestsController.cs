using AIPMS.Application.Features.Supervisors.Commands.RejectSupervisorRequest;
using AIPMS.Application.Features.Supervisors.Commands.AcceptSupervisorRequest;
using AIPMS.Application.Features.Supervisors.Commands.CancelSupervisorRequest;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/supervisor-requests")]
public sealed class SupervisorRequestsController(ISender sender) : ControllerBase
{
    [HttpPost("{id:long}/reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Reject(
        long id,
        [FromBody] RejectRequestPayload payload,
        CancellationToken cancellationToken)
    {
        await sender.Send(new RejectSupervisorRequestCommand(id, payload.ResponseMessage), cancellationToken);
        return Ok();
    }

    [HttpPost("{id:long}/accept")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Accept(
        long id,
        CancellationToken cancellationToken)
    {
        await sender.Send(new AcceptSupervisorRequestCommand(id), cancellationToken);
        return Ok();
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<ActionResult> Cancel(long id, CancellationToken cancellationToken)
    {
        await sender.Send(new CancelSupervisorRequestCommand(id), cancellationToken);
        return Ok();
    }
}

public sealed record RejectRequestPayload(string? ResponseMessage);
