using System;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class RubricApproval
{
    public long Id { get; set; }
    public long RubricId { get; set; }
    public long SubmittedBy { get; set; }
    public DateTime SubmittedAt { get; set; }
    public long? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Decision { get; set; }
    public string? Reason { get; set; }

    public Rubric Rubric { get; set; } = null!;
    public User Submitter { get; set; } = null!;
    public User? Decider { get; set; }
}
