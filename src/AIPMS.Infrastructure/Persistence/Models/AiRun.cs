namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class AiRun
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long ActorId { get; set; }
    public long? ReportId { get; set; }
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "RUNNING";
    public string ModelVersion { get; set; } = "GROUNDED_LOCAL_V1";
    public string ConfigVersion { get; set; } = "SCOPED_CONTEXT_V5_1";
    public string ScopeHash { get; set; } = "";
    public string? ResultJson { get; set; }
    public string EvidenceJson { get; set; } = "[]";
    public string? ErrorCode { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
