namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ProjectEvidenceReview
{
    public long Id { get; set; }
    public long EvidenceId { get; set; }
    public long? SourceVersionId { get; set; }
    public string SourceVersion { get; set; } = "";
    public string SourceSnapshotJson { get; set; } = "";
    public long ReviewerId { get; set; }
    public long SupervisorAssignmentId { get; set; }
    public string Decision { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime ReviewedAt { get; set; }
}
public sealed class ProjectCheckpoint
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string Title { get; set; } = "";
    public long? MajorId { get; set; }
    public DateTime PlannedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<ProjectCheckpointDecision> Decisions { get; set; } = [];
}
public sealed class ProjectCheckpointDecision
{
    public long Id { get; set; }
    public long CheckpointId { get; set; }
    public string Decision { get; set; } = "";
    public string Reason { get; set; } = "";
    public string EvidenceJson { get; set; } = "[]";
    public long DecidedBy { get; set; }
    public DateTime DecidedAt { get; set; }
}
public sealed class DefenseCommittee
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string Name { get; set; } = "";
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<DefenseCommitteeMember> Members { get; set; } = [];
}
public sealed class DefenseCommitteeMember
{
    public long CommitteeId { get; set; }
    public long UserId { get; set; }
    public long MajorId { get; set; }
}
public sealed class DefenseSession
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long CommitteeId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string Room { get; set; } = "";
    public string Status { get; set; } = "SCHEDULED";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CancellationReason { get; set; }
    public long? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
}
