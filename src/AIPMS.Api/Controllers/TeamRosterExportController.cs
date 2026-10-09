using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Application.Features.StudentRosters.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.Admin)]
[Route("api/v1/teams/export")]
public sealed class TeamRosterExportController(ITeamRosterExportService service) : ControllerBase
{
    [HttpGet]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/problem+json")]
    [ProducesResponseType<FileContentResult>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(422)]
    public async Task<IActionResult> Export([FromQuery] long semesterId, [FromQuery] long? departmentId,
        [FromQuery] long? majorId, [FromQuery] long? teamId, CancellationToken ct, [FromQuery] string format = "xlsx")
    {
        if (!string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
            throw new AIPMS.Application.Common.Exceptions.ValidationException(
                new Dictionary<string, string[]> { ["format"] = ["Only xlsx is supported."] });
        var file = await service.ExportAsync(new(semesterId, departmentId, majorId, teamId), ct);
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }
}
