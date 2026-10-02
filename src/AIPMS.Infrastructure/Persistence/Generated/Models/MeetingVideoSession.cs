namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class MeetingVideoSession
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public string Provider { get; set; } = null!;
    public string ProviderRoomKey { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long? StartedBy { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? FailureCode { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public virtual Meeting Meeting { get; set; } = null!;
    public virtual ICollection<MeetingVideoParticipantBinding> ParticipantBindings { get; set; } = new List<MeetingVideoParticipantBinding>();
    public virtual ICollection<MeetingVideoPresenceSession> PresenceSessions { get; set; } = new List<MeetingVideoPresenceSession>();
}


