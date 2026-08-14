using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class SupervisorFeedback
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long SupervisorAssignmentId { get; set; }

    public long? ProgressReportId { get; set; }

    public long? DeliverableVersionId { get; set; }

    public long? MeetingId { get; set; }

    public string FeedbackText { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual DeliverableVersion? DeliverableVersion { get; set; }

    public virtual ICollection<File> Files { get; set; } = new List<File>();

    public virtual Meeting? Meeting { get; set; }

    public virtual ProgressReport? ProgressReport { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual SupervisorAssignment SupervisorAssignment { get; set; } = null!;
}
