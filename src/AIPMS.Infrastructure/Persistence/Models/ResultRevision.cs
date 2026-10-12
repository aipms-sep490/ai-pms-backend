namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ResultCorrectionBundle
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "DRAFT";
    public string BaseStamp { get; set; } = "";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long? PublishedBy { get; set; }
    public DateTime? PublishedAt { get; set; }
    public ICollection<ResultScoreAmendment> Amendments { get; set; } = [];
}
public sealed class ResultScoreAmendment
{
    public long BundleId { get; set; }
    public long EvaluationId { get; set; }
    public long CriterionId { get; set; }
    public decimal Score { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class ResultRevision
{
    public long Id { get; set; }
    public long BundleId { get; set; }
    public long ProjectId { get; set; }
    public long TargetKey { get; set; }
    public long? StudentId { get; set; }
    public int Version { get; set; }
    public long? PreviousRevisionId { get; set; }
    public string SnapshotJson { get; set; } = "";
    public long PublishedBy { get; set; }
    public DateTime PublishedAt { get; set; }
}
