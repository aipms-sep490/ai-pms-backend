namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class FinalSubmissionRequirements
{
    public long ProjectId { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public long UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<FinalSubmissionRequirement> Items { get; set; } = new List<FinalSubmissionRequirement>();
}
public sealed class FinalSubmissionRequirement
{
    public long ProjectId { get; set; }
    public long DeliverableId { get; set; }
}
public sealed class FinalSubmission
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long ProjectPeriodId { get; set; }
    public long SubmittedBy { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime Deadline { get; set; }
    public string? Notes { get; set; }
    public Guid DraftConcurrencyToken { get; set; }
    public Guid RequirementsConcurrencyToken { get; set; }
    public ICollection<FinalSubmissionItem> Items { get; set; } = new List<FinalSubmissionItem>();
}
public sealed class FinalSubmissionItem
{
    public long SubmissionId { get; set; }
    public long DeliverableVersionId { get; set; }
    public long DeliverableId { get; set; }
    public string Title { get; set; } = "";
    public int VersionNumber { get; set; }
    public string StatusAtSubmission { get; set; } = "";
    public bool WasRequired { get; set; }
    public string FilesJson { get; set; } = "[]";
}
