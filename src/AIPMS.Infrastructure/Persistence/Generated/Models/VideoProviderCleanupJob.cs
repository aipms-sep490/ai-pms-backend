namespace AIPMS.Infrastructure.Persistence.Generated.Models;
public partial class VideoProviderCleanupJob
{
    public long Id { get; set; }
    public long MeetingVideoSessionId { get; set; }
    public string ProviderRoomKey { get; set; } = null!;
    public string Status { get; set; } = "PENDING";
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public virtual MeetingVideoSession MeetingVideoSession { get; set; } = null!;
}


