using System;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class AiCopilotSnapshot
{
    public long Id { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public string ScopeType { get; set; } = "PROJECT";
    public long ScopeId { get; set; }
    public long? ProjectId { get; set; }
    public long? DepartmentId { get; set; }
    public string Status { get; set; } = "COMPLETED";
    public string? ModelVersion { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
