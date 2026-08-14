using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class DeliverableVersion
{
    public long Id { get; set; }

    public long DeliverableId { get; set; }

    public int VersionNumber { get; set; }

    public long SubmittedBy { get; set; }

    public string? SubmissionNote { get; set; }

    public string Status { get; set; } = null!;

    public DateTime SubmittedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Deliverable Deliverable { get; set; } = null!;

    public virtual ICollection<File> Files { get; set; } = new List<File>();

    public virtual User SubmittedByNavigation { get; set; } = null!;

    public virtual ICollection<SupervisorFeedback> SupervisorFeedbacks { get; set; } = new List<SupervisorFeedback>();
}
