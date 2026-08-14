using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class ProgressReport
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long SubmittedBy { get; set; }

    public string ReportType { get; set; } = null!;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public string Summary { get; set; } = null!;

    public string? CompletedWork { get; set; }

    public string? PlannedWork { get; set; }

    public string? IssuesAndRisks { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? SubmittedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<File> Files { get; set; } = new List<File>();

    public virtual Project Project { get; set; } = null!;

    public virtual User SubmittedByNavigation { get; set; } = null!;

    public virtual ICollection<SupervisorFeedback> SupervisorFeedbacks { get; set; } = new List<SupervisorFeedback>();
}
