using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.AiSuite.Abstractions;

public interface IAiSuiteService
{
    // #1 Defense Mock Q&A, Answer Evaluation & Gate Script
    Task<DefenseMockDto> GenerateDefenseMockAsync(long projectId, DefenseMockRequest request, CancellationToken ct);
    Task<DefenseMockDto?> GetLatestDefenseMockAsync(long projectId, CancellationToken ct);
    Task<DefenseAnswerEvalDto> EvaluateDefenseAnswerAsync(long projectId, DefenseAnswerEvalRequest request, CancellationToken ct);
    Task<GateScriptDraftDto> GenerateGateScriptDraftAsync(long gateInstanceId, GateScriptDraftRequest request, CancellationToken ct);

    // #2 Auto Progress Report Draft
    Task<ProgressReportDraftDto> GenerateProgressReportDraftAsync(long projectId, ProgressReportDraftRequest request, CancellationToken ct);

    // #3 Contribution Fairness & Free-rider / Burnout
    Task<ContributionFairnessDto> AnalyzeContributionFairnessAsync(long projectId, CancellationToken ct);
    Task<ContributionFairnessDto?> GetLatestContributionFairnessAsync(long projectId, CancellationToken ct);

    // #4 Rubric Grading Copilot, Calibration & Appeal Assist
    Task<RubricGradingCopilotDto> GenerateRubricGradingCopilotAsync(long assignmentId, CancellationToken ct);
    Task<ScoreCalibrationDto> CheckScoreCalibrationAsync(long projectId, CancellationToken ct);
    Task<AppealAssistDto> AssistAppealReviewAsync(long appealId, CancellationToken ct);

    // #5 Task Breakdown & Sprint Retrospective
    Task<TaskBreakdownDto> BreakdownTaskAsync(long projectId, TaskBreakdownRequest request, CancellationToken ct);
    Task<SprintRetroDto> GenerateSprintRetroAsync(long projectId, long sprintId, CancellationToken ct);

    // #6 Pre-flight Submission Checker
    Task<SubmissionPreflightDto> CheckSubmissionPreflightAsync(long projectId, SubmissionPreflightRequest request, CancellationToken ct);

    // #7 Team Skill-Gap & Working Agreement Draft
    Task<TeamSkillGapDto> AnalyzeTeamSkillGapAsync(long teamId, TeamSkillGapRequest request, CancellationToken ct);
    Task<WorkingAgreementDraftDto> DraftWorkingAgreementAsync(long projectId, WorkingAgreementDraftRequest request, CancellationToken ct);

    // #8 Department Weekly Executive Brief
    Task<DepartmentExecutiveBriefDto> GenerateDepartmentBriefAsync(DepartmentBriefRequest request, CancellationToken ct);
    Task<DepartmentExecutiveBriefDto?> GetLatestDepartmentBriefAsync(long? departmentId, CancellationToken ct);
}
