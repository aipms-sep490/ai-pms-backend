namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class RubricVersionMetadata
{
    public long RubricId { get; set; }
    public int VersionNumber { get; set; }
    public string ApprovalStatus { get; set; } = null!;
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class EvaluationAudit
{
    public long EvaluationId { get; set; }
    public string? EvidenceSummary { get; set; }
    public long? FinalizedBy { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public string RoundingRule { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

