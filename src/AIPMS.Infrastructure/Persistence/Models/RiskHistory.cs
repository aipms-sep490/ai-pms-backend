using System;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class RiskHistory
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string RiskLevel { get; set; } = "";
    public decimal? RiskScore { get; set; }
    public int OverdueTasks { get; set; }
    public int BlockedTasks { get; set; }
    public int UnassignedTasks { get; set; }
    public decimal ProgressPercentage { get; set; }
    public DateTime RecordedAt { get; set; }

    public Project Project { get; set; } = null!;
}
