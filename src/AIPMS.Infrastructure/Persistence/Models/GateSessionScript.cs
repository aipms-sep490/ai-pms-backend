namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class GateSessionScript
{
    public long Id { get; set; }
    public long ProjectGateInstanceId { get; set; }
    public string Mode { get; set; } = "ONLINE";
    public long? RoomId { get; set; }
    public string? LiveKitRoomName { get; set; }
    public string? LiveKitSessionId { get; set; }
    public DateTimeOffset ScheduledStartAt { get; set; }
    public DateTimeOffset ScheduledEndAt { get; set; }
    public DateTimeOffset? ActualStartAt { get; set; }
    public DateTimeOffset? ActualEndAt { get; set; }
    public int? TotalDurationMinutes { get; set; }
    public string PresencesJson { get; set; } = "[]";
    public string AgendaStagesJson { get; set; } = "[]";
    public string QaLogJson { get; set; } = "[]";
    public string RemediationItemsJson { get; set; } = "[]";
    public string? GeneralNotes { get; set; }
    public string Verdict { get; set; } = "PENDING";
    public string? SignedBySecretary { get; set; }
    public string? SignedByChair { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public long? PublishedBy { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
