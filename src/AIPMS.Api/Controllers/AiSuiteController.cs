using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.AiSuite;
using AIPMS.Application.Features.AiSuite.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/ai-suite")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class AiSuiteController(IAiSuiteService suite) : ControllerBase
{
    // #1 Defense Mock Q&A, Answer Evaluation & Gate Script
    [HttpPost("projects/{projectId:long}/defense/mock-questions")]
    [ProducesResponseType<DefenseMockDto>(StatusCodes.Status200OK)]
    public Task<DefenseMockDto> GenerateDefenseMock(long projectId, [FromBody] DefenseMockRequest? request, CancellationToken ct) =>
        suite.GenerateDefenseMockAsync(projectId, request ?? new DefenseMockRequest(), ct);

    [HttpGet("projects/{projectId:long}/defense/mock-questions/latest")]
    [ProducesResponseType<DefenseMockDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<DefenseMockDto>> GetLatestDefenseMock(long projectId, CancellationToken ct)
    {
        var dto = await suite.GetLatestDefenseMockAsync(projectId, ct);
        return dto is null ? NoContent() : Ok(dto);
    }

    [HttpPost("projects/{projectId:long}/defense/evaluate-answer")]
    [ProducesResponseType<DefenseAnswerEvalDto>(StatusCodes.Status200OK)]
    public Task<DefenseAnswerEvalDto> EvaluateDefenseAnswer(long projectId, [FromBody] DefenseAnswerEvalRequest request, CancellationToken ct) =>
        suite.EvaluateDefenseAnswerAsync(projectId, request, ct);

    [HttpPost("review-gates/{gateInstanceId:long}/session-script-draft")]
    [ProducesResponseType<GateScriptDraftDto>(StatusCodes.Status200OK)]
    public Task<GateScriptDraftDto> GenerateGateScriptDraft(long gateInstanceId, [FromBody] GateScriptDraftRequest? request, CancellationToken ct) =>
        suite.GenerateGateScriptDraftAsync(gateInstanceId, request ?? new GateScriptDraftRequest(), ct);

    // #2 Auto Progress Report Draft
    [HttpPost("projects/{projectId:long}/progress-reports/draft")]
    [ProducesResponseType<ProgressReportDraftDto>(StatusCodes.Status200OK)]
    public Task<ProgressReportDraftDto> GenerateProgressReportDraft(long projectId, [FromBody] ProgressReportDraftRequest? request, CancellationToken ct) =>
        suite.GenerateProgressReportDraftAsync(projectId, request ?? new ProgressReportDraftRequest(), ct);

    // #3 Contribution Fairness & Free-rider / Burnout
    [HttpPost("projects/{projectId:long}/contributions/fairness")]
    [ProducesResponseType<ContributionFairnessDto>(StatusCodes.Status200OK)]
    public Task<ContributionFairnessDto> AnalyzeContributionFairness(long projectId, CancellationToken ct) =>
        suite.AnalyzeContributionFairnessAsync(projectId, ct);

    [HttpGet("projects/{projectId:long}/contributions/fairness/latest")]
    [ProducesResponseType<ContributionFairnessDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<ContributionFairnessDto>> GetLatestContributionFairness(long projectId, CancellationToken ct)
    {
        var dto = await suite.GetLatestContributionFairnessAsync(projectId, ct);
        return dto is null ? NoContent() : Ok(dto);
    }

    // #4 Rubric Grading Copilot, Calibration & Appeal Assist
    [HttpPost("evaluations/assignments/{assignmentId:long}/rubric-copilot")]
    [ProducesResponseType<RubricGradingCopilotDto>(StatusCodes.Status200OK)]
    public Task<RubricGradingCopilotDto> GenerateRubricGradingCopilot(long assignmentId, CancellationToken ct) =>
        suite.GenerateRubricGradingCopilotAsync(assignmentId, ct);

    [HttpPost("projects/{projectId:long}/results/calibration-check")]
    [ProducesResponseType<ScoreCalibrationDto>(StatusCodes.Status200OK)]
    public Task<ScoreCalibrationDto> CheckScoreCalibration(long projectId, CancellationToken ct) =>
        suite.CheckScoreCalibrationAsync(projectId, ct);

    [HttpPost("appeals/{appealId:long}/review-assist")]
    [ProducesResponseType<AppealAssistDto>(StatusCodes.Status200OK)]
    public Task<AppealAssistDto> AssistAppealReview(long appealId, CancellationToken ct) =>
        suite.AssistAppealReviewAsync(appealId, ct);

    [HttpPost("projects/{projectId:long}/evaluation-schemes/suggest")]
    [EnableRateLimiting("ai-assistant")]
    [ProducesResponseType<EvaluationSchemeSuggestDto>(StatusCodes.Status200OK)]
    public Task<EvaluationSchemeSuggestDto> SuggestEvaluationScheme(long projectId, CancellationToken ct) =>
        suite.SuggestEvaluationSchemeAsync(projectId, ct);

    // #5 Task Breakdown & Sprint Retrospective
    [HttpPost("projects/{projectId:long}/tasks/breakdown")]
    [ProducesResponseType<TaskBreakdownDto>(StatusCodes.Status200OK)]
    public Task<TaskBreakdownDto> BreakdownTask(long projectId, [FromBody] TaskBreakdownRequest request, CancellationToken ct) =>
        suite.BreakdownTaskAsync(projectId, request, ct);

    [HttpPost("projects/{projectId:long}/sprints/{sprintId:long}/retrospective")]
    [ProducesResponseType<SprintRetroDto>(StatusCodes.Status200OK)]
    public Task<SprintRetroDto> GenerateSprintRetro(long projectId, long sprintId, CancellationToken ct) =>
        suite.GenerateSprintRetroAsync(projectId, sprintId, ct);

    // #6 Pre-flight Submission Checker
    [HttpPost("projects/{projectId:long}/submissions/preflight")]
    [ProducesResponseType<SubmissionPreflightDto>(StatusCodes.Status200OK)]
    public Task<SubmissionPreflightDto> CheckSubmissionPreflight(long projectId, [FromBody] SubmissionPreflightRequest? request, CancellationToken ct) =>
        suite.CheckSubmissionPreflightAsync(projectId, request ?? new SubmissionPreflightRequest(), ct);

    // #7 Team Skill-Gap & Working Agreement Draft
    [HttpPost("teams/{teamId:long}/skill-gap")]
    [ProducesResponseType<TeamSkillGapDto>(StatusCodes.Status200OK)]
    public Task<TeamSkillGapDto> AnalyzeTeamSkillGap(long teamId, [FromBody] TeamSkillGapRequest? request, CancellationToken ct) =>
        suite.AnalyzeTeamSkillGapAsync(teamId, request ?? new TeamSkillGapRequest(), ct);

    [HttpPost("projects/{projectId:long}/working-agreement/draft")]
    [ProducesResponseType<WorkingAgreementDraftDto>(StatusCodes.Status200OK)]
    public Task<WorkingAgreementDraftDto> DraftWorkingAgreement(long projectId, [FromBody] WorkingAgreementDraftRequest? request, CancellationToken ct) =>
        suite.DraftWorkingAgreementAsync(projectId, request ?? new WorkingAgreementDraftRequest(), ct);

    // #8 Department Weekly Executive Brief
    [HttpPost("department/executive-brief")]
    [ProducesResponseType<DepartmentExecutiveBriefDto>(StatusCodes.Status200OK)]
    public Task<DepartmentExecutiveBriefDto> GenerateDepartmentBrief([FromBody] DepartmentBriefRequest? request, CancellationToken ct) =>
        suite.GenerateDepartmentBriefAsync(request ?? new DepartmentBriefRequest(), ct);

    [HttpGet("department/executive-brief/latest")]
    [ProducesResponseType<DepartmentExecutiveBriefDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<DepartmentExecutiveBriefDto>> GetLatestDepartmentBrief([FromQuery] long? departmentId, CancellationToken ct)
    {
        var dto = await suite.GetLatestDepartmentBriefAsync(departmentId, ct);
        return dto is null ? NoContent() : Ok(dto);
    }
}
