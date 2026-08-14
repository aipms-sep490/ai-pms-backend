using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class SupervisorAssignment
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long SupervisorProfileId { get; set; }

    public long SupervisorRequestId { get; set; }

    public bool IsPrimary { get; set; }

    public DateTime AssignedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<SupervisorFeedback> SupervisorFeedbacks { get; set; } = new List<SupervisorFeedback>();

    public virtual SupervisorProfile SupervisorProfile { get; set; } = null!;

    public virtual SupervisorRequest SupervisorRequest { get; set; } = null!;
}
