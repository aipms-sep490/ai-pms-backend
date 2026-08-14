using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class SupervisorExpertise
{
    public long Id { get; set; }

    public long SupervisorProfileId { get; set; }

    public string ExpertiseName { get; set; } = null!;

    public string? ProficiencyLevel { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual SupervisorProfile SupervisorProfile { get; set; } = null!;
}
