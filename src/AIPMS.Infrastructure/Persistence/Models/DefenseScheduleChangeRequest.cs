namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class DefenseScheduleChangeRequest
{
    public long Id { get; set; }
    public long DefenseSessionId { get; set; }
    public long RequesterId { get; set; }
    public long CurrentRoomId { get; set; }
    public DateTimeOffset CurrentStartAt { get; set; }
    public DateTimeOffset CurrentEndAt { get; set; }
    public long DesiredRoomId { get; set; }
    public DateTimeOffset DesiredStartAt { get; set; }
    public DateTimeOffset DesiredEndAt { get; set; }
    public string ReasonCategory { get; set; } = "";
    public string ReasonDetails { get; set; } = "";
    public string? EvidenceFileUrl { get; set; }
    public string Status { get; set; } = "PENDING";
    public long? ReviewerId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
}
