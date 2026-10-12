namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class MeetingNotesVersion
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public int Version { get; set; }
    public bool IsLocked { get; set; }
    public string? MeetingNotes { get; set; }
    public string? Minutes { get; set; }
    public string? Decisions { get; set; }
    public string? Blockers { get; set; }
    public string GovernanceJson { get; set; } = "{}";
    public long ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}
