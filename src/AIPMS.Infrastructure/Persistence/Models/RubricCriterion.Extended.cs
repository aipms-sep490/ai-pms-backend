using System.Collections.Generic;
using AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class RubricCriterion
{
    public string? LearningOutcome { get; set; }
    public string? EvidenceGuidance { get; set; }
    public string? TargetScope { get; set; }
    public bool IsIntegration { get; set; }
    public virtual ICollection<RubricCriterionLevel> Levels { get; set; } = new List<RubricCriterionLevel>();
}
