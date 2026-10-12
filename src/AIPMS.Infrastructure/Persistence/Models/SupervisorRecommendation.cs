namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class SupervisorRecommendation
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string EngineVersion { get; set; } = "rules-v1";
    public string AssignmentType { get; set; } = "PRIMARY";
    public long? MajorId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long TotalCount { get; set; }
    public ICollection<SupervisorRecommendationEntry> Items { get; set; } = [];
}

public sealed class SupervisorRecommendationEntry
{
    public long RunId { get; set; }
    public int Position { get; set; }
    public long SupervisorProfileId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
}
