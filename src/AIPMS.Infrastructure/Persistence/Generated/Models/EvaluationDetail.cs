using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class EvaluationDetail
{
    public long Id { get; set; }

    public long EvaluationId { get; set; }

    public long CriterionId { get; set; }

    public decimal Score { get; set; }

    public string? Comments { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Evaluation Evaluation { get; set; } = null!;

    public virtual EvaluationCriterion Criterion { get; set; } = null!;

}
