using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class ProjectTag
{
    public long ProjectId { get; set; }

    public long TagId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual Tag Tag { get; set; } = null!;
}
