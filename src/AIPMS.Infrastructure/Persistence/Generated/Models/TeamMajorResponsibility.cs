using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class TeamMajorResponsibility
{
    public long Id { get; set; }

    public long TeamId { get; set; }

    public long MajorId { get; set; }

    public string Content { get; set; } = null!;

    public int SortOrder { get; set; }

    public Guid ConcurrencyToken { get; set; }

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}
