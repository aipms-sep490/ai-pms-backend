using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ReviewGateChecklistTemplate
{
    public long Id { get; set; }
    public long AcademicSemesterId { get; set; }
    public string TrackCode { get; set; } = "IT_ONLY";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "DRAFT";
    public long LeadDepartmentId { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public sealed class ProjectGateChecklist
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long GateInstanceId { get; set; }
    public long? TemplateId { get; set; }
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "DRAFT";
    public string AdvisorRecommendation { get; set; } = "PENDING";
    public string ReviewerRecommendation { get; set; } = "PENDING";
    public string Review3ConsensusStatus { get; set; } = "PENDING_RECOMMENDATIONS";
    public long? PublishedBy { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<ProjectGateChecklistItem> Items { get; set; } = [];
}

public sealed class ProjectGateChecklistItem
{
    public long Id { get; set; }
    public long ChecklistId { get; set; }
    public string ItemCode { get; set; } = "";
    public string Section { get; set; } = "";
    public string TrackScope { get; set; } = "COMMON";
    public long? MajorId { get; set; }
    public string ConditionTag { get; set; } = "NONE";
    public string CoreQuestion { get; set; } = "";
    public string Priority { get; set; } = "Mandatory";
    public decimal Weight { get; set; } = 1.00m;
    public bool IsApplicable { get; set; } = true;
    public string? NaReason { get; set; }
    public string? TailoredCheckpoints { get; set; }
    public string? TailoredEvidence { get; set; }
    public string? ReviewerFocusPrompt { get; set; }
    public string? SourceRef { get; set; }
    public string? AdvisoryStatus { get; set; }
    public string? AdvisoryNotes { get; set; }
    public long? AdvisoryBy { get; set; }
    public decimal? ReviewerScore { get; set; }
    public string? ReviewerNotes { get; set; }
    public long? ReviewerBy { get; set; }
}
