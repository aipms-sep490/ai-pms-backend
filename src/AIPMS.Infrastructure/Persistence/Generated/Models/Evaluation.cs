using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Evaluation
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long EvaluatorId { get; set; }

    public string EvaluationType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public decimal? TotalScore { get; set; }

    public string? Comments { get; set; }

    public DateTime? EvaluatedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<EvaluationDetail> EvaluationDetails { get; set; } = new List<EvaluationDetail>();

    public virtual User Evaluator { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;

}
