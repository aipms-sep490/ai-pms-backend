namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class WeeklyAssessment
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long StudentId { get; set; }
    public long MajorId { get; set; }
    public long AcademicSemesterId { get; set; }
    public int WeekNumber { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string TimeZoneId { get; set; } = "";
    public int Version { get; set; }
    public string Status { get; set; } = "DRAFT";
    public long RubricId { get; set; }
    public string RubricJson { get; set; } = "";
    public decimal? TotalScore { get; set; }
    public string? Comments { get; set; }
    public string? RevisionReason { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public long PrimaryAssignmentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public List<WeeklyAssessmentScore> Scores { get; set; } = [];
}
public sealed class WeeklyAssessmentScore
{
    public long AssessmentId { get; set; }
    public long RubricCriterionId { get; set; }
    public decimal? Score { get; set; }
    public string? Comments { get; set; }
}
