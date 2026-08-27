using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class RolePermission
{
    public long RoleId { get; set; }

    public long PermissionId { get; set; }

    public long? AssignedBy { get; set; }

    public DateTime AssignedAt { get; set; }

    public virtual User? AssignedByNavigation { get; set; }

    public virtual Permission Permission { get; set; } = null!;

    public virtual Role Role { get; set; } = null!;
}
