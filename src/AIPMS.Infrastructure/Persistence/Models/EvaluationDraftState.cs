namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class EvaluationDraftState
{
    public long EvaluationId { get; set; }
    public long AssignmentId { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public string CalculationRule { get; set; } = "WEIGHTED_10_AWAY_FROM_ZERO_2DP_V1";
}
