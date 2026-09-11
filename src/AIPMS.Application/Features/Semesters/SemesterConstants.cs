namespace AIPMS.Application.Features.Semesters;

/// <summary>
/// Valid status values for AcademicSemester and ProjectPeriod (mirror DB CHECK constraints).
/// </summary>
internal static class SemesterStatuses
{
    public const string Draft = "DRAFT";
    public const string Upcoming = "UPCOMING";
    public const string Active = "ACTIVE";
    public const string Closed = "CLOSED";
    public const string Archived = "ARCHIVED";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Draft, Upcoming, Active, Closed, Archived
        };
}

/// <summary>
/// Valid period_type values for ProjectPeriod (mirror DB CHECK constraint).
/// </summary>
internal static class PeriodTypes
{
    public const string Registration = "REGISTRATION";
    public const string ProjectReview = "PROJECT_REVIEW";
    public const string SupervisorSelection = "SUPERVISOR_SELECTION";
    public const string Execution = "EXECUTION";
    public const string FinalSubmission = "FINAL_SUBMISSION";
    public const string Evaluation = "EVALUATION";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Registration, ProjectReview, SupervisorSelection,
            Execution, FinalSubmission, Evaluation
        };
}
