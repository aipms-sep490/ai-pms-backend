namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ProjectResultPolicy
{
    public long ProjectId { get; set; }
    public decimal PassThreshold { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public long UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<ProjectResultPolicyItem> Items { get; set; } = new List<ProjectResultPolicyItem>();
}
public sealed class ProjectResultPolicyItem
{
    public long ProjectId { get; set; }
    public long AssignmentId { get; set; }
    public decimal WeightPercent { get; set; }
}
public sealed class ProjectResult
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long FinalSubmissionId { get; set; }
    public long PublishedBy { get; set; }
    public DateTime PublishedAt { get; set; }
    public string SnapshotJson { get; set; } = "";
    public ICollection<ProjectResultEvaluation> Evaluations { get; set; } = new List<ProjectResultEvaluation>();
}
public sealed class ProjectResultEvaluation
{
    public long ResultId { get; set; }
    public long EvaluationId { get; set; }
}
