namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ResultAppealPolicy
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public int Version { get; set; }
    public string ApprovalReference { get; set; } = "";
    public long RecordedBy { get; set; }
    public DateTime RecordedAt { get; set; }
}
public sealed class ResultAppealWindow
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long PolicyId { get; set; }
    public DateTime OpensAt { get; set; }
    public DateTime ClosesAt { get; set; }
    public string Status { get; set; } = "OPEN";
    public string TargetsJson { get; set; } = "[]";
    public long CreatedBy { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
public sealed class ResultAppealRequest
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long PolicyId { get; set; }
    public long? WindowId { get; set; }
    public string Source { get; set; } = "STUDENT_APPEAL";
    public long TargetKey { get; set; }
    public long? StudentId { get; set; }
    public int ResultVersion { get; set; }
    public long ComponentId { get; set; }
    public string Scope { get; set; } = "";
    public long? MajorId { get; set; }
    public long ResponsibleDepartmentId { get; set; }
    public string FrozenInputs { get; set; } = "";
    public string BaseStamp { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public long RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public string Reason { get; set; } = "";
    public long? ReviewerId { get; set; }
    public string? Proposal { get; set; }
    public string? ReviewReason { get; set; }
    public long? BundleId { get; set; }
    public long? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public ICollection<ResultAppealFile> Files { get; set; } = [];
}
public sealed class ResultAppealFile
{
    public long RequestId { get; set; }
    public long FileId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
}
public sealed class ResultAppealEvent
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public long ActorId { get; set; }
    public long? BundleId { get; set; }
    public string Action { get; set; } = "";
    public DateTime At { get; set; }
    public string? Reason { get; set; }
}
