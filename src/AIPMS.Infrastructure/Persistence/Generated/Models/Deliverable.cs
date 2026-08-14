using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Deliverable
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long? MilestoneId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? DeliverableType { get; set; }

    public DateTime? DueAt { get; set; }

    public string Status { get; set; } = null!;

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<DeliverableVersion> DeliverableVersions { get; set; } = new List<DeliverableVersion>();

    public virtual Milestone? Milestone { get; set; }

    public virtual Project Project { get; set; } = null!;
}
