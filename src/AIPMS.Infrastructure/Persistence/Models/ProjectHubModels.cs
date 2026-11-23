namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ProjectPinnedResource
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string ResourceType { get; set; } = "OTHER";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class EvidenceAnchoredComment
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long? GateInstanceId { get; set; }
    public long? EvidenceId { get; set; }
    public string AnchorRef { get; set; } = "";
    public string Severity { get; set; } = "INFO";
    public string Status { get; set; } = "OPEN";
    public string Content { get; set; } = "";
    public long CreatedBy { get; set; }
    public long? ResolvedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public sealed class EvidenceRubricCriterionLink
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long EvidenceId { get; set; }
    public long RubricCriterionId { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
