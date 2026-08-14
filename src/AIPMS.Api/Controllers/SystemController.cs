using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> Get() =>
        Ok(new SystemInfoResponse("AI-PMS API", "v1", "ok"));
}

public sealed record SystemInfoResponse(string Name, string Version, string Status);
