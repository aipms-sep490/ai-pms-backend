using System;

namespace AIPMS.Domain.Entities;

public sealed class SupervisorRequest
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long SupervisorProfileId { get; set; }
    public long RequestedBy { get; set; }
    public string Status { get; set; } = "PENDING";
    public string? RequestMessage { get; set; }
    public string? ResponseMessage { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
