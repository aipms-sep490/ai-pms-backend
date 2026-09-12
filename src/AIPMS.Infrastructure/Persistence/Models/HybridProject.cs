using AIPMS.Domain.Teams;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class TeamAcademicConfiguration
{
    public long TeamId { get; set; }
    public string ProjectMode { get; set; } = "SINGLE_MAJOR";
    public long? PrimaryMajorId { get; set; }
    public long LeadDepartmentId { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public List<TeamMajorRequirement> Requirements { get; set; } = [];
    public TeamAcademicScope ToScope() => new(ProjectMode, PrimaryMajorId, LeadDepartmentId,
        Requirements.OrderBy(r => r.MajorId).Select(r => new MajorRequirement(r.MajorId, r.MinMembers, r.MaxMembers, r.Responsibility)).ToArray(), ConcurrencyToken);
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
