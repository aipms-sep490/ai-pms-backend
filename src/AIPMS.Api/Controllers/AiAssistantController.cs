using System.Threading;
using System.Threading.Tasks;
using AIPMS.Api.Filters;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.AiAssistant.Queries;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
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
public sealed class AiAssistantController(
    ISender sender,
    AIPMS.Application.Features.AiAssistant.Abstractions.IAiRunStore runs,
    IAcademicKnowledgeService knowledge,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("/api/v1/projects/{projectId:long}/ai/digest")]
    [AiFeatureGate(AiFeatureArea.ProgressRisk)]
    [ProducesResponseType<ProjectAiDigestDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectAiDigestDto>> Digest(
        long projectId,
        [FromServices] IScopedProgressAnalysisService analysis,
        CancellationToken cancellationToken)
    {
        var result = await analysis.AnalyzeAsync(projectId, false, cancellationToken);
        var progress = result.ProgressSummary;
        var actions = result.Recommendations ?? Array.Empty<string>();
        var digest = new ProjectAiDigestDto(
            result.ProjectId,
            result.GeneratedAtUtc,
            result.DataStatus,
            result.RiskLevel,
            result.RiskScore,
            new ProgressDigestSummary(progress.TotalMilestones, progress.CompletedMilestones,
                progress.TotalTasks, progress.DoneTasks, progress.BlockedTasks,
                progress.OverdueTasks, progress.ProgressPercentage),
            result.Signals,
            actions,
            result.ProjectEvidence,
            result.Limitations);
        return Ok(digest);
    }

    [HttpPost("/api/v1/projects/{projectId:long}/ai/progress-analysis")]
    [AiFeatureGate(AiFeatureArea.ProgressRisk)]
    [ProducesResponseType<AIPMS.Application.Features.Projects.DTOs.ProjectProgressAnalysisDto>(200)]
    public Task<AIPMS.Application.Features.Projects.DTOs.ProjectProgressAnalysisDto> AnalyzeProgress(long projectId,
        [FromServices] AIPMS.Application.Features.Projects.Abstractions.IScopedProgressAnalysisService analysis, CancellationToken ct)
        => analysis.AnalyzeAsync(projectId, true, ct);
    [HttpGet("/api/v1/projects/{projectId:long}/ai/runs")]
    [AiFeatureGate(AiFeatureArea.Assistant)]
    [ProducesResponseType<AIPMS.Application.Common.Models.PagedResult<AiRunDto>>(200)]
    public Task<AIPMS.Application.Common.Models.PagedResult<AiRunDto>> History(long projectId, CancellationToken ct, int page = 1, int pageSize = 20) => runs.ListAsync(projectId, page, pageSize, ct);
    [HttpGet("/api/v1/projects/{projectId:long}/ai/runs/{id:long}")]
    [AiFeatureGate(AiFeatureArea.Assistant)]
    [ProducesResponseType<AiRunDto>(200)]
    public Task<AiRunDto> Run(long projectId, long id, CancellationToken ct) => runs.GetAsync(projectId, id, ct);
    [HttpGet("/api/v1/projects/{projectId:long}/reports/{reportId:long}/summary")]
    [HttpGet("/api/v1/projects/{projectId:long}/ai/reports/{reportId:long}/summary")]
    [AiFeatureGate(AiFeatureArea.Assistant)]
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
    [AiFeatureGate(AiFeatureArea.Assistant)]
    [ProducesResponseType<ProjectAssistantResponseDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectAssistantResponseDto>> Ask(
        long projectId,
        [FromBody] AskProjectAssistantRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProjectId.HasValue && request.ProjectId != projectId)
            return BadRequest(new ProblemDetails { Title = "ProjectId must match the route project." });
        var opts = configuration.GetSection(AIPMS.AI.Configuration.AiAssistantOptions.SectionName).Get<AIPMS.AI.Configuration.AiAssistantOptions>()
            ?? new AIPMS.AI.Configuration.AiAssistantOptions();
        if (!string.IsNullOrWhiteSpace(request.Scope) && !string.Equals(request.Scope, "PROJECT", StringComparison.OrdinalIgnoreCase))
        {
            if (!opts.Enabled || !opts.KnowledgeEnabled)
                throw new AIPMS.Application.Common.Exceptions.ServiceUnavailableException(
                    "Academic knowledge AI is disabled by system configuration.",
                    AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.AiFeatureDisabled);
            return Ok(await knowledge.AskAsync(new AcademicAssistantAskRequest(request.Query, request.Scope, projectId), cancellationToken));
        }
        if (!opts.Enabled || !opts.LegacyProjectAskEnabled)
            throw new AIPMS.Application.Common.Exceptions.ServiceUnavailableException(
                "Legacy project AI ask is disabled by system configuration.",
                AIPMS.Application.Common.Exceptions.WorkflowErrorCodes.AiFeatureDisabled);
        var result = await sender.Send(new AskProjectAssistantQuery(projectId, request.Query), cancellationToken);
        return Ok(result);
    }
}
