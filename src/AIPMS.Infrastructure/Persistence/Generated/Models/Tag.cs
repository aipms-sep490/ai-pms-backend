using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Tag
{
    public long Id { get; set; }

    public string Name { get; set; } = null!;

    public string NormalizedName { get; set; } = null!;

    public string TagType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<ProjectTag> ProjectTags { get; set; } = new List<ProjectTag>();
}
