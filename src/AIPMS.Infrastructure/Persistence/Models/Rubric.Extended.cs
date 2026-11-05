using System;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Rubric
{
    public long? MajorId { get; set; }
    public string? ProjectMode { get; set; }
    public string? EvaluationStage { get; set; }
    public string? EvaluationScope { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? ApprovalReference { get; set; }

    public virtual Major? Major { get; set; }
}
