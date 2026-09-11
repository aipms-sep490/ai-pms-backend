namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class EvaluationAssignment
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long EvaluatorId { get; set; }
    public long RubricId { get; set; }
    public long ProjectPeriodId { get; set; }
    public long DepartmentId { get; set; }
    public string EvaluationType { get; set; } = "LECTURER";
    public string Status { get; set; } = "ACTIVE";
    public long AssignedBy { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public Guid ConcurrencyToken { get; set; }
}
