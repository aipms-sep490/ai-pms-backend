namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class AiDeliverableReviewRun
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long DeliverableId { get; set; }
    public long DeliverableVersionId { get; set; }
    public long RubricId { get; set; }
    public int? RubricVersion { get; set; }
    public long? ChecklistId { get; set; }
    public int? ChecklistVersion { get; set; }
    public string EvidenceSnapshot { get; set; } = "{}";
}

public sealed class AiDeliverableReviewCriterion
{
    public long RunId { get; set; }
    public long CriterionId { get; set; }
    public string CriterionName { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class AiDeliverableReviewFinding
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public long CriterionId { get; set; }
    public string Kind { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class AiDeliverableReviewCitation
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public string Title { get; set; } = "";
    public string Version { get; set; } = "";
    public string Section { get; set; } = "";
}
