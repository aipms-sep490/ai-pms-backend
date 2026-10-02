namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class MeetingVideoPresenceSession
{
    public long Id { get; set; }
    public long MeetingVideoSessionId { get; set; }
    public long UserId { get; set; }
    public string ProviderParticipantIdentity { get; set; } = null!;
    public string? ProviderConnectionId { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
    public string? DisconnectReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public virtual MeetingVideoSession MeetingVideoSession { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}


