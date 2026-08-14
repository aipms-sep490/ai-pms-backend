using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class SupervisorRequest
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long SupervisorProfileId { get; set; }

    public long RequestedBy { get; set; }

    public string Status { get; set; } = null!;

    public string? RequestMessage { get; set; }

    public string? ResponseMessage { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual User RequestedByNavigation { get; set; } = null!;

    public virtual SupervisorAssignment? SupervisorAssignment { get; set; }

    public virtual SupervisorProfile SupervisorProfile { get; set; } = null!;
}
