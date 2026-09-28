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
}
