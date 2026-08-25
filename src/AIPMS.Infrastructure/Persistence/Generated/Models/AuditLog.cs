using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class AuditLog
{
    public long Id { get; set; }

    public long? ActorUserId { get; set; }

    public string Action { get; set; } = null!;

    public string EntityType { get; set; } = null!;

    public string? EntityId { get; set; }

    public string Outcome { get; set; } = null!;

    public Guid? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? DetailsJson { get; set; }

    public DateTime OccurredAt { get; set; }

    public virtual User? ActorUser { get; set; }
}
