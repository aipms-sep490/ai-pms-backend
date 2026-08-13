using AIPMS.Application.Abstractions.AI;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Route("api/ai/insights")]
public sealed class AiInsightsController(IProgressAnalysisService progressAnalysis) : ControllerBase
{
    [HttpPost("progress")]
    [ProducesResponseType<ProgressAnalysisResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<ProgressAnalysisResult> AnalyzeProgress(ProgressAnalysisInput input) =>
        Ok(progressAnalysis.Analyze(input));
}
