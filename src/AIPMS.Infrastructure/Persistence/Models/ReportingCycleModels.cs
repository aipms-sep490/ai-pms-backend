using System;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ProgressReportPeriod
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long ProjectPeriodId { get; set; }
    public string ReportType { get; set; } = null!;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime Deadline { get; set; }
    public string LatePolicy { get; set; } = "BLOCK";
    public Guid ConcurrencyToken { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public ProjectPeriod ProjectPeriod { get; set; } = null!;
    public User Creator { get; set; } = null!;
}

public sealed class ProjectActionItem
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string SourceType { get; set; } = null!;
    public long? MeetingId { get; set; }
    public long? ProgressReportId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public long? OwnerId { get; set; }
    public long? TaskId { get; set; }
    public long? MilestoneId { get; set; }
    public DateTime? DueAt { get; set; }
    public string Status { get; set; } = "TODO";
    public Guid ConcurrencyToken { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public Meeting? Meeting { get; set; }
    public ProgressReport? ProgressReport { get; set; }
    public User? Owner { get; set; }
    public AIPMS.Infrastructure.Persistence.Generated.Models.Task? Task { get; set; }
    public Milestone? Milestone { get; set; }
    public User Creator { get; set; } = null!;
}
