namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class FinalSubmissionDraft
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long ProjectPeriodId { get; set; }
    public string? Notes { get; set; }
    public long CreatedBy { get; set; }
    public long UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public ICollection<FinalSubmissionDraftItem> Items { get; set; } = new List<FinalSubmissionDraftItem>();
}

public sealed class FinalSubmissionDraftItem
{
    public long DraftId { get; set; }
    public long DeliverableVersionId { get; set; }
}
