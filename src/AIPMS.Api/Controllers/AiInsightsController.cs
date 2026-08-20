using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.ProgressReports.Commands.AnalyzeProgress;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/ai/insights")]
public sealed class AiInsightsController(ISender sender) : ControllerBase
{
    [HttpPost("progress")]
    [ProducesResponseType<ProgressAnalysisResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProgressAnalysisResult>> AnalyzeProgress(
        AnalyzeProgressCommand command,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(command, cancellationToken));
}
