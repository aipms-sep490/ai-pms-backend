using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.AiAssistant.Queries;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AcademicKnowledge.Abstractions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("ai-assistant")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
public sealed class AiAssistantController(ISender sender, AIPMS.Application.Features.AiAssistant.Abstractions.IAiRunStore runs, IAcademicKnowledgeService knowledge) : ControllerBase
{
    [HttpPost("/api/v1/projects/{projectId:long}/ai/progress-analysis")]
    [ProducesResponseType<AIPMS.Application.Features.Projects.DTOs.ProjectProgressAnalysisDto>(200)]
    public Task<AIPMS.Application.Features.Projects.DTOs.ProjectProgressAnalysisDto> AnalyzeProgress(long projectId,
        [FromServices] AIPMS.Application.Features.Projects.Abstractions.IScopedProgressAnalysisService analysis, CancellationToken ct)
        => analysis.AnalyzeAsync(projectId, true, ct);
    [HttpGet("/api/v1/projects/{projectId:long}/ai/runs")]
    [ProducesResponseType<AIPMS.Application.Common.Models.PagedResult<AiRunDto>>(200)]
    public Task<AIPMS.Application.Common.Models.PagedResult<AiRunDto>> History(long projectId, CancellationToken ct, int page = 1, int pageSize = 20) => runs.ListAsync(projectId, page, pageSize, ct);
    [HttpGet("/api/v1/projects/{projectId:long}/ai/runs/{id:long}")]
    [ProducesResponseType<AiRunDto>(200)]
    public Task<AiRunDto> Run(long projectId, long id, CancellationToken ct) => runs.GetAsync(projectId, id, ct);
    [HttpGet("/api/v1/projects/{projectId:long}/reports/{reportId:long}/summary")]
    [HttpGet("/api/v1/projects/{projectId:long}/ai/reports/{reportId:long}/summary")]
    [ProducesResponseType<ReportSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReportSummaryDto>> SummarizeReport(
        long projectId,
        long reportId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SummarizeProgressReportQuery(projectId, reportId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("/api/v1/projects/{projectId:long}/ai/assistant/ask")]
    [HttpPost("/api/v1/projects/{projectId:long}/ai/ask")]
    [ProducesResponseType<ProjectAssistantResponseDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectAssistantResponseDto>> Ask(
        long projectId,
        [FromBody] AskProjectAssistantRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Scope) && !string.Equals(request.Scope, "PROJECT", StringComparison.OrdinalIgnoreCase))
            return Ok(await knowledge.AskAsync(new AcademicAssistantAskRequest(request.Query, request.Scope, request.ProjectId ?? projectId), cancellationToken));
        var result = await sender.Send(new AskProjectAssistantQuery(projectId, request.Query), cancellationToken);
        return Ok(result);
    }
}
