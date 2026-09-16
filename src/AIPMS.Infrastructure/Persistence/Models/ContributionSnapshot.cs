using System;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ContributionSnapshot
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long UserId { get; set; }
    public DateTime SnapshotAt { get; set; }
    public string SnapshotHash { get; set; } = null!;
    public double ActivityScore { get; set; }
    public int EvidenceCount { get; set; }
    public string SnapshotJson { get; set; } = null!;
}
