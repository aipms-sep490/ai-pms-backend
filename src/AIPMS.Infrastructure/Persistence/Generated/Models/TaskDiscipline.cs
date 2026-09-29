using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class TaskDiscipline
{
    public long TaskId { get; set; }

    public long MajorId { get; set; }

    public string Role { get; set; } = null!;

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}
