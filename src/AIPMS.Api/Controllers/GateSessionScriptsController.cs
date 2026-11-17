using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.ReviewGates.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1/review-gates/{id:long}")]
public sealed class GateSessionScriptsController(IGateSessionScriptService service) : ControllerBase
{
    [HttpGet("session-script")]
    public Task<GateSessionScriptDto> Get(long id, CancellationToken ct) => service.Get(id, ct);

    [HttpPut("session-script")]
    public Task<GateSessionScriptDto> Save(long id, SaveGateSessionScriptRequest request, CancellationToken ct) => service.Save(id, request, ct);

    [HttpPost("livekit-presence/sync")]
    public Task<GateSessionScriptDto> Sync(long id, SyncGatePresenceRequest request, CancellationToken ct) => service.Sync(id, request, ct);
}
