using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class TeamEligibilityCheck
{
    public long Id { get; set; }

    public long TeamId { get; set; }

    public long ProjectPeriodId { get; set; }

    public long? ProjectId { get; set; }

    public string RoundType { get; set; } = null!;

    public long? RevisionHistoryId { get; set; }

    public string ProjectMode { get; set; } = null!;

    public string PolicyVersion { get; set; } = null!;

    public string RuleVersion { get; set; } = null!;

    public string RosterHash { get; set; } = null!;

    public string AcademicScopeHash { get; set; } = null!;

    public string ProjectContextHash { get; set; } = null!;

    public string Fingerprint { get; set; } = null!;

    public string TemporalStateHash { get; set; } = null!;

    public string EvaluationKey { get; set; } = null!;

    public string Result { get; set; } = null!;

    public DateTime? ValidUntilAt { get; set; }

    public long CheckedBy { get; set; }

    public DateTime CheckedAt { get; set; }

    public string TriggerSource { get; set; } = null!;

    public virtual ICollection<TeamEligibilityIssue> TeamEligibilityIssues { get; set; } = new List<TeamEligibilityIssue>();
}
