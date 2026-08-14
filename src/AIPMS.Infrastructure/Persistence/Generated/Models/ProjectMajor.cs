using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class ProjectMajor
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long MajorId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Major Major { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;
}
