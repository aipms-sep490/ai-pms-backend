namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class MeetingVideoParticipantBinding
{
    public long Id { get; set; }
    public long MeetingVideoSessionId { get; set; }
    public long UserId { get; set; }
    public string ProviderParticipantIdentity { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public virtual MeetingVideoSession MeetingVideoSession { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}


