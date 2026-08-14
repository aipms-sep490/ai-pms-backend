using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Task
{
    public long Id { get; set; }

    public long MilestoneId { get; set; }

    public long? ParentTaskId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string Status { get; set; } = null!;

    public string? Priority { get; set; }

    public DateTime? StartAt { get; set; }

    public DateTime? DueAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<Task> InverseParentTask { get; set; } = new List<Task>();

    public virtual Milestone Milestone { get; set; } = null!;

    public virtual Task? ParentTask { get; set; }

    public virtual ICollection<TaskAssignee> TaskAssignees { get; set; } = new List<TaskAssignee>();

    public virtual ICollection<TaskDependency> TaskDependencyDependsOnTasks { get; set; } = new List<TaskDependency>();

    public virtual ICollection<TaskDependency> TaskDependencyTasks { get; set; } = new List<TaskDependency>();

    public virtual ICollection<TaskStatusHistory> TaskStatusHistories { get; set; } = new List<TaskStatusHistory>();
}
