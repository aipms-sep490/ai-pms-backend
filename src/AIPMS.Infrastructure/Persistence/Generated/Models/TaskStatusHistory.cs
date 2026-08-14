using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class TaskStatusHistory
{
    public long Id { get; set; }

    public long TaskId { get; set; }

    public string? OldStatus { get; set; }

    public string NewStatus { get; set; } = null!;

    public long ChangedBy { get; set; }

    public string? Reason { get; set; }

    public DateTime ChangedAt { get; set; }

    public virtual User ChangedByNavigation { get; set; } = null!;

    public virtual Task Task { get; set; } = null!;
}
