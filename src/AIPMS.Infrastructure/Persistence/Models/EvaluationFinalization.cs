namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class EvaluationFinalization
{
    public long EvaluationId { get; set; }
    public long FinalSubmissionId { get; set; }
    public long FinalizedBy { get; set; }
    public DateTime FinalizedAt { get; set; }
    public string SnapshotJson { get; set; } = "";
}
