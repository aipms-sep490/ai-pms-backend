using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class ProjectEvidence
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public long? MajorId { get; set; }

    public string SourceType { get; set; } = null!;

    public long SourceId { get; set; }

    public long? TaskId { get; set; }

    public long? DeliverableId { get; set; }

    public long? MeetingId { get; set; }

    public long? ProgressReportId { get; set; }

    public long? FileId { get; set; }

    public string? Notes { get; set; }

    public string VerificationStatus { get; set; } = null!;

    public long SubmittedBy { get; set; }

    public DateTime SubmittedAt { get; set; }
}
