using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public class RubricCriterionLevel
{
    public long Id { get; set; }
    public long RubricCriterionId { get; set; }
    public int LevelOrder { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Descriptor { get; set; } = string.Empty;
    public decimal? ScoreFrom { get; set; }
    public decimal? ScoreTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual RubricCriterion RubricCriterion { get; set; } = null!;
}
