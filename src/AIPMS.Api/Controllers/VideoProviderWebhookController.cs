using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/integrations/video/livekit/webhook")]
public sealed class VideoProviderWebhookController(IVideoProviderEventService events) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(401)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        await events.ProcessLiveKitAsync(body, Request.Headers.Authorization.ToString(), cancellationToken);
        return NoContent();
    }
}


