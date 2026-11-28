namespace AIPMS.Application.Common.Exceptions;

public static class WorkflowErrorCodes
{
    public const string FinalPackageRequired = "FINAL_PACKAGE_REQUIRED";
    public const string EvaluationWindowClosed = "EVALUATION_WINDOW_CLOSED";
    public const string PublishedRubricRequired = "PUBLISHED_RUBRIC_REQUIRED";
    public const string EvaluatorIneligible = "EVALUATOR_INELIGIBLE";
    public const string SupervisorRequired = "SUPERVISOR_REQUIRED";
    public const string StaleConcurrencyToken = "STALE_CONCURRENCY_TOKEN";
    public const string FinalizedAssignment = "FINALIZED_ASSIGNMENT";
    public const string ArchiveNotAllowed = "ARCHIVE_NOT_ALLOWED";
    public const string TaskDependencyBlocked = "TASK_DEPENDENCY_BLOCKED";
    public const string TaskAssigneesLocked = "TASK_ASSIGNEES_LOCKED";
    public const string TaskReopenReasonRequired = "TASK_REOPEN_REASON_REQUIRED";
    public const string TaskDependencyImpactPending = "TASK_DEPENDENCY_IMPACT_PENDING";

    // Deliverables
    public const string ExecutionWindowRequired = "EXECUTION_WINDOW_REQUIRED";
    public const string ProjectNotActive = "PROJECT_NOT_ACTIVE";
    public const string DeliverableLocked = "DELIVERABLE_LOCKED";
    public const string DeliverableDeadlinePassed = "DELIVERABLE_DEADLINE_PASSED";
    public const string StaleLatestVersion = "STALE_LATEST_VERSION";
    public const string NotLatestPendingVersion = "NOT_LATEST_PENDING_VERSION";
    public const string MilestoneNotInProject = "MILESTONE_NOT_IN_PROJECT";
    public const string DeliverableHasSubmittedHistory = "DELIVERABLE_HAS_SUBMITTED_HISTORY";

    // Milestones, Tasks & Execution Window
    public const string TaskOutsideMilestone = "TASK_OUTSIDE_MILESTONE";
    public const string TaskOutsideExecutionWindow = "TASK_OUTSIDE_EXECUTION_WINDOW";
    public const string MilestoneOutsideExecutionWindow = "MILESTONE_OUTSIDE_EXECUTION_WINDOW";
    public const string ParentDateConflict = "PARENT_DATE_CONFLICT";
    public const string MilestoneDependencyCycle = "MILESTONE_DEPENDENCY_CYCLE";
    public const string MilestoneDependencyOrderViolation = "MILESTONE_DEPENDENCY_ORDER_VIOLATION";
    public const string MilestoneDependencyNotSelf = "MILESTONE_DEPENDENCY_NOT_SELF";
    public const string MilestoneDependencySameProject = "MILESTONE_DEPENDENCY_SAME_PROJECT";
    public const string ReportDeadlineInvalid = "REPORT_DEADLINE_INVALID";

    // Progress Reports
    public const string ReportAlreadyExistsForCycle = "REPORT_ALREADY_EXISTS_FOR_CYCLE";
    public const string ReportAlreadyExistsForPeriod = "REPORT_ALREADY_EXISTS_FOR_PERIOD";
    public const string ReportNotEditable = "REPORT_NOT_EDITABLE";
    public const string ReportAlreadySubmitted = "REPORT_ALREADY_SUBMITTED";
    public const string ReportCycleRequired = "REPORT_CYCLE_REQUIRED";
    public const string ReportDeadlinePassed = "REPORT_DEADLINE_PASSED";
    public const string ReportFeedbackOnDraftNotAllowed = "REPORT_FEEDBACK_ON_DRAFT_NOT_ALLOWED";

    // Core V4.0 Security, Governance & Evaluation Invariants
    public const string EvaluatorRoleConflict = "EVALUATOR_ROLE_CONFLICT";
    public const string SupervisorRoleConflict = "SUPERVISOR_ROLE_CONFLICT";
    public const string MemberMajorNotAllowed = "MEMBER_MAJOR_NOT_ALLOWED";
    public const string ProjectModeNotAllowedByPeriod = "PROJECT_MODE_NOT_ALLOWED_BY_PERIOD";
    public const string StudentAlreadyInTeam = "STUDENT_ALREADY_IN_TEAM";
    public const string InvalidAcademicScope = "INVALID_ACADEMIC_SCOPE";
    public const string AssignmentRevoked = "ASSIGNMENT_REVOKED";
    public const string ScopeAccessDenied = "SCOPE_ACCESS_DENIED";
    public const string RegistrationWindowClosed = "REGISTRATION_WINDOW_CLOSED";
    public const string IncompleteScores = "INCOMPLETE_SCORES";
    public const string RestrictedDelete = "RESTRICTED_DELETE";
    public const string Gate3NotPassed = "GATE_3_NOT_PASSED";
    public const string BlueprintNotPublished = "BLUEPRINT_NOT_PUBLISHED";
    public const string ImmutableVersion = "IMMUTABLE_VERSION";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string StaleEvidenceVersion = "STALE_EVIDENCE_VERSION";
    public const string IntegrationDeliverableRequired = "INTEGRATION_DELIVERABLE_REQUIRED";
    public const string AttributionLimitsRequired = "ATTRIBUTION_LIMITS_REQUIRED";
    public const string StaleReviewRound = "STALE_REVIEW_ROUND";
    public const string EvidenceRequired = "EVIDENCE_REQUIRED";
    public const string CircularDependency = "CIRCULAR_DEPENDENCY";
    public const string InvalidInitialStatus = "INVALID_INITIAL_STATUS";
    public const string ReportLocked = "REPORT_LOCKED";
    public const string AcademicProfileLocked = "ACADEMIC_PROFILE_LOCKED";
    public const string InvalidSpecializationForScope = "INVALID_SPECIALIZATION_FOR_SCOPE";
    public const string SelfEvaluationForbidden = "SELF_EVALUATION_FORBIDDEN";
    public const string InvalidCategoryForMajor = "INVALID_CATEGORY_FOR_MAJOR";
    public const string UnresolvedCriticalComments = "UNRESOLVED_CRITICAL_COMMENTS";
    public const string ColdWindowNotCompleted = "COLD_WINDOW_NOT_COMPLETED";
    public const string CouncilQuorumUnmet = "COUNCIL_QUORUM_UNMET";
    public const string ScheduleConflict = "SCHEDULE_CONFLICT";
    public const string RateLimited = "RATE_LIMITED";
    public const string FileQuarantined = "FILE_QUARANTINED";
    public const string AiFeatureDisabled = "AI_FEATURE_DISABLED";
    public const string AiDailyQuotaExceeded = "AI_DAILY_QUOTA_EXCEEDED";
    public const string RbacPermissionMatrixReadOnly = "RBAC_PERMISSION_MATRIX_READ_ONLY";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InternalServerError = "INTERNAL_SERVER_ERROR";
}
