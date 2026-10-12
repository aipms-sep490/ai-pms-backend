namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Meeting
{
    public Guid? SeriesId { get; set; }
    public int? OccurrenceNumber { get; set; }
    public string Recurrence { get; set; } = "NONE";
    public int NotesVersion { get; set; }
    public DateTime? NotesLockedAt { get; set; }
    public long? NotesLockedBy { get; set; }
}
public partial class MeetingParticipant
{
    public string RsvpStatus { get; set; } = "INVITED";
    public Guid RsvpToken { get; set; } = Guid.NewGuid();
}
