using System;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class TeamLeaderChangeRequest
{
    public long Id { get; set; }
    public long TeamId { get; set; }
    public long ProjectId { get; set; }
    public long RequestedBy { get; set; }
    public long CurrentLeaderUserId { get; set; }
    public long NewLeaderUserId { get; set; }
    public long MentorProfileId { get; set; }
    public string Status { get; set; } = null!;
    public string? RequestMessage { get; set; }
    public string? ResponseMessage { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual Project Project { get; set; } = null!;
    public virtual Team Team { get; set; } = null!;
    public virtual User RequestedByNavigation { get; set; } = null!;
    public virtual User CurrentLeaderUser { get; set; } = null!;
    public virtual User NewLeaderUser { get; set; } = null!;
    public virtual SupervisorProfile MentorProfile { get; set; } = null!;
}

