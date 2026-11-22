namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class PeriodPolicyVersion
{
    public long Id { get; set; }
    public long ProjectPeriodId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = "DRAFT";
    public DateTime EffectiveFrom { get; set; }
    public DateTime EffectiveTo { get; set; }
    public string SnapshotJson { get; set; } = "";
    public Guid ConcurrencyToken { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed class PeriodPolicyUsage
{
    public long Id { get; set; }
    public long PolicyVersionId { get; set; }
    public string EntityType { get; set; } = "";
    public long EntityId { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed class EvaluationScheme
{
    public string? GovernanceJson { get; set; }
    public DateTime? LockedAt { get; set; }
    public string CalculationRule { get; set; } = AIPMS.Application.Features.Evaluations.Services.EvaluationSchemeRules.CalculationRule;
    public long Id { get; set; }
    public long? RootId { get; set; }
    public int Version { get; set; }
    public long ProjectId { get; set; }
    public long ProjectPeriodId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "DRAFT";
    public decimal PassThreshold { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public long? PolicyVersionId { get; set; }
    public string StudentsJson { get; set; } = "[]";
    public string RegistrationSnapshotJson { get; set; } = "";
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? PublishedBy { get; set; }
    public DateTime? PublishedAt { get; set; }
    public List<EvaluationSchemeComponent> Components { get; set; } = [];
}
public sealed class EvaluationSchemeComponent
{
    public string? Stage { get; set; }
    public string? EvaluatorWeightsJson { get; set; }
    public long Id { get; set; }
    public long SchemeId { get; set; }
    public string Name { get; set; } = "";
    public string Scope { get; set; } = "";
    public long? MajorId { get; set; }
    public long RubricId { get; set; }
    public decimal ProjectWeightPercent { get; set; }
    public decimal StudentWeightPercent { get; set; }
    public int RequiredEvaluators { get; set; }
    public string? TargetScope { get; set; }
    public bool IsIntegration { get; set; }
}
public sealed class StudentResult
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long StudentId { get; set; }
    public long MajorId { get; set; }
    public long SchemeId { get; set; }
    public decimal TotalScore { get; set; }
    public decimal PassThreshold { get; set; }
    public string Outcome { get; set; } = "";
    public string? OutcomeDetail { get; set; }
    public string CalculationRule { get; set; } = "";
    public long PublishedBy { get; set; }
    public DateTime PublishedAt { get; set; }
    public string SnapshotJson { get; set; } = "";
}
public sealed class StudentResultEvaluation
{
    public long ResultId { get; set; }
    public long EvaluationId { get; set; }
}
public sealed class GateIndividualProcessScore
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long GateInstanceId { get; set; }
    public long StudentId { get; set; }
    public long SupervisorAssignmentId { get; set; }
    public decimal Score { get; set; }
    public decimal GateWeightPercent { get; set; }
    public string? MentorReferenceNotes { get; set; }
    public string? Comments { get; set; }
    public string Status { get; set; } = "DRAFT";
    public Guid ConcurrencyToken { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
}
public sealed class StudentDefenseAbsence
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long StudentId { get; set; }
    public long RecordedBy { get; set; }
    public string Reason { get; set; } = "";
    public string? MinutesReference { get; set; }
    public DateTime RecordedAt { get; set; }
}
