using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class ProjectPeriod
{
    public long Id { get; set; }

    public long AcademicSemesterId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string PeriodType { get; set; } = null!;

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AcademicSemester AcademicSemester { get; set; } = null!;
}
