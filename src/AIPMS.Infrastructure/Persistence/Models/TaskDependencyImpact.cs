using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class TaskDependencyImpact
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public long PredecessorTaskId { get; set; }
    public long ReopenHistoryId { get; set; }
    public string Status { get; set; } = "PENDING";
    public DateTime ReopenedAt { get; set; }
    public long ReopenedBy { get; set; }
    public string ReopenReason { get; set; } = string.Empty;
    public DateTime? ReviewedAt { get; set; }
    public long? ReviewedBy { get; set; }
    public string? ReviewReason { get; set; }
    public M.Task Task { get; set; } = null!;
    public M.Task PredecessorTask { get; set; } = null!;
    public M.TaskStatusHistory ReopenHistory { get; set; } = null!;
}
