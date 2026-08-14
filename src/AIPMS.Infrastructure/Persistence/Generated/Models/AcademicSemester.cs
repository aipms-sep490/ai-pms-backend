using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class AcademicSemester
{
    public long Id { get; set; }

    public long OrganizationId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Organization Organization { get; set; } = null!;

    public virtual ICollection<ProjectPeriod> ProjectPeriods { get; set; } = new List<ProjectPeriod>();

    public virtual ICollection<Rubric> Rubrics { get; set; } = new List<Rubric>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
}
