using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.WeeklyAssessments;

public sealed class WeeklyAssessmentSettings
{
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
}
public sealed record WeeklyAssessmentWeek(int WeekNumber, DateOnly StartDate, DateOnly EndDate, bool HasStarted);
public sealed record WeeklyAssessmentCalendar(long AcademicSemesterId, string TimeZoneId, IReadOnlyList<WeeklyAssessmentWeek> Weeks);
public sealed record SaveWeeklyAssessmentRequest(long StudentId, int WeekNumber, long RubricId, int ExpectedLatestVersion,
    IReadOnlyList<EvaluationScoreInput> Scores, string? Comments = null, string? ConcurrencyToken = null, string? RevisionReason = null);
public sealed record SubmitWeeklyAssessmentRequest(string ConcurrencyToken);
public sealed record WeeklyAssessmentDto(long Id, long ProjectId, long StudentId, long MajorId, long AcademicSemesterId,
    int WeekNumber, DateOnly StartDate, DateOnly EndDate, string TimeZoneId, int Version, string Status,
    long RubricId, string RubricName, int RubricVersion, IReadOnlyList<EvaluationScoreRecord> Scores, decimal? TotalScore,
    string? Comments, string? RevisionReason, string ConcurrencyToken, long CreatedBy, long PrimaryAssignmentId,
    DateTime CreatedAt, DateTime? SubmittedAt, bool CanEdit, bool CanSubmit,
    bool IsDemo = true, bool AffectsOfficialResult = false);
