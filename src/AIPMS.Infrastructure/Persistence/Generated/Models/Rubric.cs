using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Rubric
{
    public long Id { get; set; }

    public long? DepartmentId { get; set; }

    public long? AcademicSemesterId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AcademicSemester? AcademicSemester { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual Department? Department { get; set; }

    public virtual ICollection<Evaluation> Evaluations { get; set; } = new List<Evaluation>();

    public virtual ICollection<RubricCriterion> RubricCriteria { get; set; } = new List<RubricCriterion>();
}
