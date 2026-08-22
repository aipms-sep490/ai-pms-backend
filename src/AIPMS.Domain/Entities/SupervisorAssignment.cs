using System;

namespace AIPMS.Domain.Entities;

public sealed class SupervisorAssignment
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long SupervisorProfileId { get; set; }
    public long SupervisorRequestId { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
