using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class TeamInvitation
{
    public long Id { get; set; }

    public long TeamId { get; set; }

    public long InvitedUserId { get; set; }

    public long InvitedBy { get; set; }

    public string Status { get; set; } = null!;

    public string? Message { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User InvitedByNavigation { get; set; } = null!;

    public virtual User InvitedUser { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
