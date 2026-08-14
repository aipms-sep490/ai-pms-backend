using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Major
{
    public long Id { get; set; }

    public long DepartmentId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Department Department { get; set; } = null!;

    public virtual ICollection<ProjectMajor> ProjectMajors { get; set; } = new List<ProjectMajor>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
