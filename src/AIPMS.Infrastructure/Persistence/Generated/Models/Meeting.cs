using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Meeting
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Title { get; set; } = null!;

    public string? Agenda { get; set; }

    public string? MeetingNotes { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public string? Location { get; set; }

    public string? OnlineUrl { get; set; }

    public string Status { get; set; } = null!;

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public string MeetingDeliveryMode { get; set; } = "ONSITE";
    public string VideoChannel { get; set; } = "NONE";

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<File> Files { get; set; } = new List<File>();

    public virtual ICollection<MeetingParticipant> MeetingParticipants { get; set; } = new List<MeetingParticipant>();
    public virtual ICollection<MeetingVideoSession> MeetingVideoSessions { get; set; } = new List<MeetingVideoSession>();

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<SupervisorFeedback> SupervisorFeedbacks { get; set; } = new List<SupervisorFeedback>();
}
