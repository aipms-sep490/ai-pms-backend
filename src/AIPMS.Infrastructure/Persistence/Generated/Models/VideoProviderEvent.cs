namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class VideoProviderEvent
{
    public long Id { get; set; }
    public string Provider { get; set; } = null!;
    public string ProviderEventId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public long? MeetingVideoSessionId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string PayloadHash { get; set; } = null!;
    public string ProcessingStatus { get; set; } = null!;
    public string? ErrorCode { get; set; }
    public virtual MeetingVideoSession? MeetingVideoSession { get; set; }
}


