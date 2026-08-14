using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Team
{
    public long Id { get; set; }

    public long AcademicSemesterId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string Status { get; set; } = null!;

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AcademicSemester AcademicSemester { get; set; } = null!;

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual Project? Project { get; set; }

    public virtual ICollection<TeamInvitation> TeamInvitations { get; set; } = new List<TeamInvitation>();

    public virtual ICollection<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();
}
