namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class MeetingDecision
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public string Content { get; set; } = "";
    public long DecidedBy { get; set; }
    public DateTime DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class MeetingActionItem
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public long? AssigneeUserId { get; set; }
    public DateTime? DueAt { get; set; }
    public string Status { get; set; } = "";
    public Guid ConcurrencyToken { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
