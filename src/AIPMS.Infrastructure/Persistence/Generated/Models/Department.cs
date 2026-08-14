using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Department
{
    public long Id { get; set; }

    public long OrganizationId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Major> Majors { get; set; } = new List<Major>();

    public virtual Organization Organization { get; set; } = null!;

    public virtual ICollection<Rubric> Rubrics { get; set; } = new List<Rubric>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
