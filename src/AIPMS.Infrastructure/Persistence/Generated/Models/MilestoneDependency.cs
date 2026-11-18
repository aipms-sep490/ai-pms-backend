using System;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class MilestoneDependency
{
    public long Id { get; set; }

    public long MilestoneId { get; set; }

    public long DependsOnMilestoneId { get; set; }

    public string DependencyType { get; set; } = "FINISH_TO_START";

    public DateTime CreatedAt { get; set; }

    public virtual Milestone Milestone { get; set; } = null!;

    public virtual Milestone DependsOnMilestone { get; set; } = null!;
}
