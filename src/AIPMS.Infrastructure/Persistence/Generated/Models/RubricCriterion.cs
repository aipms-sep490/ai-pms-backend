using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class RubricCriterion
{
    public long Id { get; set; }

    public long RubricId { get; set; }

    public long CriterionId { get; set; }

    public decimal WeightPercent { get; set; }

    public decimal MaxScore { get; set; }

    public int SortOrder { get; set; }

    public bool IsRequired { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual EvaluationCriterion Criterion { get; set; } = null!;

    public virtual Rubric Rubric { get; set; } = null!;
}
