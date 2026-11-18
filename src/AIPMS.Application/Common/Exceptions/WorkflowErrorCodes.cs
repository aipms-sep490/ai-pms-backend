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
}
