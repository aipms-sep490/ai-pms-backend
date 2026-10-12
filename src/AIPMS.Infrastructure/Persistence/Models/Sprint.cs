namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class Sprint
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "PLANNED";
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
public sealed class TaskBoardLabel
{
    public long TaskId { get; set; }
    public string Label { get; set; } = "";
}
public sealed class SprintTaskEvent
{
    public long Id { get; set; }
    public long SprintId { get; set; }
    public long TaskId { get; set; }
    public string Kind { get; set; } = "";
    public bool InScope { get; set; }
    public string TaskStatus { get; set; } = "";
    public int? StoryPoints { get; set; }
    public long ActorId { get; set; }
    public DateTime OccurredAt { get; set; }
}
