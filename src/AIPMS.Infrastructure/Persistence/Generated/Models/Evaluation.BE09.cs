namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Evaluation
{
    public string? EvidenceSummary { get; set; }
    public long? FinalizedBy { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public string RoundingRule { get; set; } = null!;
    public byte[] RowVersion { get; set; } = null!;
    public virtual User? FinalizedByNavigation { get; set; }
}
