using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class TaskDependency
{
    public long Id { get; set; }

    public long TaskId { get; set; }

    public long DependsOnTaskId { get; set; }

    public string DependencyType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Task DependsOnTask { get; set; } = null!;

    public virtual Task Task { get; set; } = null!;
}
