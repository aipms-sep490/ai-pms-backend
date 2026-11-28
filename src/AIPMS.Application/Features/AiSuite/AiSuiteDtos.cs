using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.AiSuite;

// =========================================================================
// Shared Snapshot Summary
// =========================================================================
public sealed record AiSnapshotMetaDto(
    long? SnapshotId,
    string FeatureKey,
    string Status,
    string? ModelVersion,
    DateTime GeneratedAt,
    string? CreatedByName,
    bool IsSimulatedFallback = false);

// =========================================================================
// #1 Defense Mock Q&A, Answer Evaluation & Gate Session Script Draft
// =========================================================================
public sealed record DefenseMockRequest(
    string? RoleFocus = null, // ALL | CHAIR | REVIEWER | SECRETARY
    int QuestionCount = 6,
    string? FocusTopic = null);

public sealed record DefenseMockQuestionDto(
    string Id,
    string CommitteeRole,
    string Difficulty, // EASY | MEDIUM | HARD
    string Category,
    string Question,
    string WhyAsked,
    string SuggestedAnswerOutline,
    IReadOnlyList<string> KeyPointsToMention,
    string? TargetStudentName);

public sealed record DefenseMockDto(
    long ProjectId,
    string Headline,
    string ReadinessAssessment,
    IReadOnlyList<string> WeakSpots,
    IReadOnlyList<DefenseMockQuestionDto> Questions,
    AiSnapshotMetaDto Meta);

public sealed record DefenseAnswerEvalRequest(
    string Question,
    string StudentAnswer,
    string? CommitteeRole = null,
    string? SuggestedOutline = null);

public sealed record DefenseAnswerEvalDto(
    long ProjectId,
    decimal Score, // 0..10
    string Verdict, // STRONG | ADEQUATE | NEEDS_IMPROVEMENT
    string Feedback,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> MissingPoints,
    string ImprovedAnswerSample,
    string? FollowUpProbe,
    AiSnapshotMetaDto Meta);

public sealed record GateScriptDraftRequest(string? FocusNote = null);

public sealed record GateScriptAgendaDraftDto(string Stage, int DurationMinutes, string Purpose);

public sealed record GateScriptQuestionDraftDto(
    string AskerRole,
    string Question,
    string? TargetMemberName,
    string VerificationGoal);

public sealed record GateScriptDraftDto(
    long GateInstanceId,
    long ProjectId,
    string GateCode,
    string ExecutiveBrief,
    IReadOnlyList<string> StrengthHighlights,
    IReadOnlyList<string> RiskWatchlist,
    IReadOnlyList<GateScriptAgendaDraftDto> AgendaStages,
    IReadOnlyList<GateScriptQuestionDraftDto> SuggestedQuestions,
    IReadOnlyList<string> RemediationWatchlist,
    string GeneralNotesDraft,
    AiSnapshotMetaDto Meta);

// =========================================================================
// #2 Auto Progress Report Draft
// =========================================================================
public sealed record ProgressReportDraftRequest(
    string ReportType = "WEEKLY",
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    string? AdditionalNote = null);

public sealed record ProgressReportDraftEvidenceDto(
    string SourceType, // TASK | MEETING_AI | DELIVERABLE | MILESTONE
    string Title,
    string Detail);

public sealed record ProgressReportDraftDto(
    long ProjectId,
    string ReportType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Summary,
    string CompletedWork,
    string InProgressWork,
    string PlannedWork,
    string IssuesAndRisks,
    IReadOnlyList<ProgressReportDraftEvidenceDto> Evidence,
    AiSnapshotMetaDto Meta);

// =========================================================================
// #3 Multi-dimensional Contribution Fairness & Free-rider / Burnout
// =========================================================================
public sealed record MemberFairnessMetricDto(
    long UserId,
    string FullName,
    string Role,
    int TasksAssigned,
    int TasksCompleted,
    int TasksOverdue,
    int CriteriaCompleted,
    int CommentCount,
    int MeetingsAttended,
    int SpeechSegments,
    double SpeechSharePercent,
    decimal? AverageWeeklyScore,
    double? AveragePeerRating,
    string Signal, // BALANCED | OVERLOADED_BURNOUT | LOW_ENGAGEMENT_RISK
    string Assessment,
    IReadOnlyList<string> EvidenceBullets);

public sealed record ContributionFairnessDto(
    long ProjectId,
    int BalanceScore, // 0..100
    string Headline,
    string ExecutiveSummary,
    bool IncludesPrivateSignals,
    IReadOnlyList<MemberFairnessMetricDto> Members,
    IReadOnlyList<string> RebalanceRecommendations,
    IReadOnlyList<string> SupervisorVerificationQuestions,
    AiSnapshotMetaDto Meta);

// =========================================================================
// #4 Rubric Grading Copilot, Score Calibration & Appeal Assist
// =========================================================================
public sealed record RubricCriterionSuggestionDto(
    long RubricCriterionId,
    string CriterionName,
    decimal WeightPercent,
    decimal? MaxScore,
    string SuggestedLevel,
    decimal? SuggestedScoreMin,
    decimal? SuggestedScoreMax,
    string SuggestedComment,
    IReadOnlyList<string> EvidenceCitations,
    string Confidence); // HIGH | MEDIUM | LOW

public sealed record RubricGradingCopilotDto(
    long AssignmentId,
    long ProjectId,
    string RubricName,
    string OverallSummary,
    IReadOnlyList<RubricCriterionSuggestionDto> Criteria,
    IReadOnlyList<string> VerificationQuestions,
    string Disclaimer,
    AiSnapshotMetaDto Meta,
    bool AntiAnchoringEnforced = true);

public sealed record ScoreCalibrationAlertDto(
    string Severity, // HIGH | MEDIUM | LOW
    string Category, // DISCREPANCY | COMMENT_MISMATCH | INCOMPLETE | OUTLIER
    string Title,
    string Detail,
    string? CriterionName);

public sealed record ScoreCalibrationDto(
    long ProjectId,
    int EvaluatedCount,
    string OverallStatus, // CONSISTENT | REVIEW_RECOMMENDED | HIGH_VARIANCE
    string Summary,
    IReadOnlyList<ScoreCalibrationAlertDto> Alerts,
    IReadOnlyList<string> Recommendations,
    AiSnapshotMetaDto Meta);

public sealed record AppealAssistDto(
    long AppealId,
    long ProjectId,
    string AppealSummary,
    string MeritAssessment, // LIKELY_MERIT | NEEDS_MANUAL_CHECK | WEAK_BASIS
    IReadOnlyList<string> KeyClaims,
    IReadOnlyList<string> EvidenceChecks,
    string DraftResponse,
    AiSnapshotMetaDto Meta);

// =========================================================================
// #5 Task Breakdown (TaskCriteria) & Sprint Retrospective
// =========================================================================
public sealed record TaskBreakdownRequest(
    long? TaskId = null,
    string? Title = null,
    string? Description = null,
    long? MilestoneId = null);

public sealed record TaskBreakdownCriterionDto(
    string Title,
    string VerificationHint);

public sealed record TaskAssigneeRecommendationDto(
    long UserId,
    string FullName,
    string? MajorName,
    int OpenTaskCount,
    string Reason);

public sealed record TaskBreakdownDto(
    long ProjectId,
    string TaskTitle,
    string SuggestedPriority,
    int SuggestedStoryPoints,
    string ImplementationSummary,
    IReadOnlyList<TaskBreakdownCriterionDto> Criteria,
    IReadOnlyList<string> PotentialRisks,
    IReadOnlyList<string> RelatedExistingTasks,
    IReadOnlyList<TaskAssigneeRecommendationDto> RecommendedAssignees,
    AiSnapshotMetaDto Meta);

public sealed record SprintRetroDto(
    long ProjectId,
    long SprintId,
    string SprintName,
    int TotalTasks,
    int CompletedTasks,
    int OverdueOrIncompleteTasks,
    string Headline,
    string ExecutiveSummary,
    IReadOnlyList<string> WentWell,
    IReadOnlyList<string> NeedsImprovement,
    IReadOnlyList<string> Bottlenecks,
    IReadOnlyList<string> NextSprintActions,
    AiSnapshotMetaDto Meta);

// =========================================================================
// #6 Pre-flight Submission Checker (FinalSubmission / COLD / ReviewGate)
// =========================================================================
public sealed record SubmissionPreflightRequest(
    string Target = "FINAL_SUBMISSION", // FINAL_SUBMISSION | COLD | REVIEW_GATE
    long? TargetId = null);

public sealed record SubmissionPreflightCheckDto(
    string Item,
    string Status, // PASS | WARN | FAIL
    string Detail,
    string? FixSuggestion);

public sealed record SubmissionPreflightDto(
    long ProjectId,
    string Target,
    int ReadinessScore, // 0..100
    string Verdict, // READY | WARNING | BLOCKING_ISSUES
    string Summary,
    IReadOnlyList<SubmissionPreflightCheckDto> Checks,
    IReadOnlyList<string> PriorityFixes,
    AiSnapshotMetaDto Meta);

// =========================================================================
// #7 Interdisciplinary Team Skill-Gap & Working Agreement Draft
// =========================================================================
public sealed record TeamSkillGapRequest(
    long? TopicId = null,
    long? ProjectId = null,
    string? CustomTopicDescription = null);

public sealed record SkillCoverageItemDto(
    string SkillOrRole,
    string Status, // COVERED | WEAK | MISSING
    IReadOnlyList<string> CoveredByMembers,
    string Note);

public sealed record MemberRoleSuggestionDto(
    long UserId,
    string FullName,
    string? MajorName,
    string SuggestedRole,
    IReadOnlyList<string> Strengths);

public sealed record TeamSkillGapDto(
    long TeamId,
    string TeamName,
    string ProjectMode,
    int ReadinessScore, // 0..100
    string Summary,
    IReadOnlyList<SkillCoverageItemDto> CoverageMatrix,
    IReadOnlyList<MemberRoleSuggestionDto> RoleSuggestions,
    IReadOnlyList<string> OnboardingRecommendations,
    AiSnapshotMetaDto Meta);

public sealed record WorkingAgreementDraftRequest(string? TeamPreferences = null);

public sealed record WorkingAgreementSectionDraftDto(string Title, string Body);

public sealed record WorkingAgreementDraftDto(
    long ProjectId,
    string Summary,
    IReadOnlyList<WorkingAgreementSectionDraftDto> Sections,
    AiSnapshotMetaDto Meta);

// =========================================================================
// #8 Department Weekly Executive Brief
// =========================================================================
public sealed record DepartmentBriefRequest(
    long? SemesterId = null,
    long? DepartmentId = null);

public sealed record DepartmentRiskProjectDto(
    long ProjectId,
    string ProjectCode,
    string ProjectTitle,
    string RiskLevel, // CRITICAL | HIGH | MEDIUM
    int OverdueTaskCount,
    int BlockedTaskCount,
    int DaysSinceLastMeeting,
    string Reason,
    string RecommendedAction);

public sealed record DepartmentSupervisorLoadDto(
    long UserId,
    string FullName,
    int ActiveProjectCount,
    string Note);

public sealed record DepartmentExecutiveBriefDto(
    long? DepartmentId,
    string ScopeName,
    int TotalActiveProjects,
    int AtRiskProjectCount,
    int SilentTeamCount,
    int PendingAppealsCount,
    string Headline,
    string ExecutiveSummary,
    IReadOnlyList<DepartmentRiskProjectDto> TopRiskProjects,
    IReadOnlyList<DepartmentSupervisorLoadDto> SupervisorAlerts,
    IReadOnlyList<string> WeeklyPriorityActions,
    AiSnapshotMetaDto Meta);

// #9 Evaluation Scheme AI Suggest (advisory draft from real PUBLISHED Rubrics)
public sealed record EvaluationSchemeSuggestComponentDto(
    string Name,
    string Scope,
    long? MajorId,
    string? MajorName,
    long RubricId,
    string RubricCode,
    string RubricName,
    string Stage,
    decimal ProjectWeightPercent,
    decimal StudentWeightPercent,
    int RequiredEvaluators,
    string Rationale,
    bool IsIntegration = false);

public sealed record EvaluationSchemeSuggestDto(
    long ProjectId,
    string SuggestedName,
    decimal SuggestedPassThreshold,
    string Summary,
    bool UsedAi,
    IReadOnlyList<EvaluationSchemeSuggestComponentDto> Components,
    IReadOnlyList<string> Warnings,
    AiSnapshotMetaDto Meta,
    string? CalculationVersion = null,
    decimal? FailingComponentFloor = null,
    string? TrackCode = null);
