using AIPMS.Domain.Teams;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class TeamAcademicConfiguration
{
    public long TeamId { get; set; }
    public string ProjectMode { get; set; } = "SINGLE_MAJOR";
    public long? PrimaryMajorId { get; set; }
    public long LeadDepartmentId { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public Guid? ResponsibilityVersion { get; set; }
    public string? TrackCode { get; set; }
    public bool HasIntegrationDeliverable { get; set; }
    public string? MktAttributionLimitsNote { get; set; }
    public List<TeamMajorRequirement> Requirements { get; set; } = [];
    public TeamAcademicScope ToScope() => new(ProjectMode, PrimaryMajorId, LeadDepartmentId,
        Requirements.OrderBy(r => r.MajorId).Select(r => new MajorRequirement(r.MajorId, r.MinMembers, r.MaxMembers, r.Responsibility)).ToArray(),
        ConcurrencyToken, TrackCode, HasIntegrationDeliverable, MktAttributionLimitsNote);
}

public sealed class TeamMajorRequirement
{
    public long TeamId { get; set; }
    public long MajorId { get; set; }
    public int MinMembers { get; set; }
    public int MaxMembers { get; set; }
    public string Responsibility { get; set; } = string.Empty;
}

public sealed class ProjectRegistrationSnapshot
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long ProjectPeriodId { get; set; }
    public long LeadDepartmentId { get; set; }
    public long SubmittedBy { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public bool IsLeadOverride { get; set; }
    public string? OverrideReason { get; set; }
    public long? OverriddenBy { get; set; }
    public DateTime? OverriddenAt { get; set; }
    public string? ConsolidatedRevisionNotes { get; set; }
    public List<ProjectDepartmentDecision> Decisions { get; set; } = [];
}

public sealed class ProjectDepartmentDecision
{
    public long SnapshotId { get; set; }
    public long DepartmentId { get; set; }
    public string Decision { get; set; } = "PENDING";
    public long? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Reason { get; set; }
}

public sealed class ProjectGateRoundScopeDecision
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long GateInstanceId { get; set; }
    public int RoundNumber { get; set; }
    public string Scope { get; set; } = "COMMON";
    public long? DepartmentId { get; set; }
    public long? MajorId { get; set; }
    public string Decision { get; set; } = "PENDING";
    public string? Notes { get; set; }
    public long? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

public sealed class WorkflowSlaNotification
{
    public long Id { get; set; }
    public string EntityType { get; set; } = "PROJECT_REVIEW";
    public long EntityId { get; set; }
    public int RoundNumber { get; set; } = 1;
    public long DepartmentId { get; set; }
    public string SlaTier { get; set; } = "REMINDER_48H";
    public DateTime SentAt { get; set; }
}
