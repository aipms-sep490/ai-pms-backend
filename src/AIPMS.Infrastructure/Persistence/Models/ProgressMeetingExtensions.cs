namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ProgressReportPeriod
{
    public long Id { get; set; } public long ProjectId { get; set; } public string ReportType { get; set; } = null!;
    public DateOnly PeriodStart { get; set; } public DateOnly PeriodEnd { get; set; } public DateTime DeadlineAt { get; set; }
    public string LatePolicy { get; set; } = null!; public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; }
}
public sealed class ProgressReportMetadata
{
    public long ReportId { get; set; } public long ReportPeriodId { get; set; } public bool IsLate { get; set; }
    public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; }
}
public sealed class ProgressReportSection
{
    public long Id { get; set; } public long ReportId { get; set; } public string SectionType { get; set; } = null!;
    public string Content { get; set; } = null!; public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; }
}
public sealed class ProgressReportContribution
{
    public long Id { get; set; } public long ReportId { get; set; } public long ContributorId { get; set; }
    public string SectionType { get; set; } = null!; public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; }
}
public sealed class MeetingDecision
{
    public long Id { get; set; } public long MeetingId { get; set; } public string Content { get; set; } = null!;
    public long CreatedBy { get; set; } public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; }
}
public sealed class MeetingBlocker
{
    public long Id { get; set; } public long MeetingId { get; set; } public string Content { get; set; } = null!;
    public long CreatedBy { get; set; } public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; }
}
public sealed class MeetingActionItem
{
    public long Id { get; set; } public long MeetingId { get; set; } public string Title { get; set; } = null!;
    public string? Description { get; set; } public long OwnerUserId { get; set; } public DateOnly? DueDate { get; set; }
    public string Status { get; set; } = null!; public long? TaskId { get; set; } public long? MilestoneId { get; set; }
    public long CreatedBy { get; set; } public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; }
}
