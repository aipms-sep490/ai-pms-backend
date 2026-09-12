using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ProjectTopic
{
    public long Id { get; set; }
    public long ProjectPeriodId { get; set; }
    public long LeadDepartmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = "DRAFT";
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ProblemStatement { get; set; }
    public string? Objectives { get; set; }
    public string? ExpectedOutput { get; set; }
    public string? Domain { get; set; }
    public string TechnologiesJson { get; set; } = "[]";
    public string KeywordsJson { get; set; } = "[]";
    public string ProjectMode { get; set; } = "SINGLE_MAJOR";
    public long? PrimaryMajorId { get; set; }
    public long CreatedBy { get; set; }
    public long UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long? PublishedBy { get; set; }
    public DateTime? PublishedAt { get; set; }
    public long? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public M.ProjectPeriod Period { get; set; } = null!;
    public M.Department LeadDepartment { get; set; } = null!;
    public List<TopicMajorRequirement> Requirements { get; set; } = [];
}

public sealed class TopicMajorRequirement
{
    public long TopicId { get; set; }
    public long MajorId { get; set; }
    public long DepartmentId { get; set; }
    public int MinMembers { get; set; }
    public int MaxMembers { get; set; }
    public string Responsibility { get; set; } = string.Empty;
    public M.Major Major { get; set; } = null!;
    public M.Department Department { get; set; } = null!;
}
