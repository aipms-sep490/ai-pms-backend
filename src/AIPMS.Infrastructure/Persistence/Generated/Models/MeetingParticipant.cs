using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class MeetingParticipant
{
    public long Id { get; set; }

    public long MeetingId { get; set; }

    public long UserId { get; set; }

    public string? AttendanceStatus { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Meeting Meeting { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
