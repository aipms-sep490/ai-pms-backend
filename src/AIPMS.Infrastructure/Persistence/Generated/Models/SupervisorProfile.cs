using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class SupervisorProfile
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public string? Bio { get; set; }

    public int? MaxActiveProjects { get; set; }

    public bool IsAvailable { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<SupervisorAssignment> SupervisorAssignments { get; set; } = new List<SupervisorAssignment>();

    public virtual ICollection<SupervisorExpertise> SupervisorExpertises { get; set; } = new List<SupervisorExpertise>();

    public virtual ICollection<SupervisorRequest> SupervisorRequests { get; set; } = new List<SupervisorRequest>();

    public virtual User User { get; set; } = null!;
}
