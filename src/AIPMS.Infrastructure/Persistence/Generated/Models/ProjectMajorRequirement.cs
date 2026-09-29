using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class ProjectMajorRequirement
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long MajorId { get; set; }

    public int MinMembers { get; set; }

    public int MaxMembers { get; set; }

    public string Responsibility { get; set; } = null!;

    public Guid ConcurrencyToken { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
