using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class AcademicBlueprint
{
    public long Id { get; set; }
    public long AcademicSemesterId { get; set; }
    public string TrackCode { get; set; } = "IT_ONLY";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "DRAFT";
    public long LeadDepartmentId { get; set; }
    public long? ParticipatingDepartmentId { get; set; }
    public long? PeriodPolicyVersionId { get; set; }
    public long? GateTemplateId { get; set; }
    public long? MilestoneTemplateId { get; set; }
    public long? RubricId { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public ICollection<AcademicBlueprintApproval> Approvals { get; set; } = [];
}

public sealed class AcademicBlueprintApproval
{
    public long Id { get; set; }
    public long BlueprintId { get; set; }
    public long DepartmentId { get; set; }
    public string ScopeRole { get; set; } = "LEAD";
    public string Decision { get; set; } = "PENDING";
    public long? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class ProjectBlueprintSnapshot
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long BlueprintId { get; set; }
    public int BlueprintVersion { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public long AppliedBy { get; set; }
    public DateTime AppliedAt { get; set; }
}

public sealed class GateDeadlineChangeRequest
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long GateInstanceId { get; set; }
    public DateTime RequestedSubmissionDueAt { get; set; }
    public DateTime RequestedReviewEndAt { get; set; }
    public DateTime RequestedRevisionDueAt { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public long RequestedBy { get; set; }
    public long? ReviewedBy { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
