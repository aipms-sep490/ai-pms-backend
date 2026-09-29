using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class TeamEligibilityIssue
{
    public long Id { get; set; }

    public long EligibilityCheckId { get; set; }

    public int SortOrder { get; set; }

    public string RuleCode { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public long? MajorId { get; set; }

    public long? UserId { get; set; }

    public string? ExpectedValue { get; set; }

    public string? ActualValue { get; set; }

    public string Message { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual TeamEligibilityCheck EligibilityCheck { get; set; } = null!;
}
