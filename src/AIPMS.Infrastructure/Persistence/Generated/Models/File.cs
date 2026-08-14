using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class File
{
    public long Id { get; set; }

    public long UploadedBy { get; set; }

    public long? DeliverableVersionId { get; set; }

    public long? ProgressReportId { get; set; }

    public long? MeetingId { get; set; }

    public long? SupervisorFeedbackId { get; set; }

    public string OriginalFileName { get; set; } = null!;

    public string? StoredFileName { get; set; }

    public string StoragePath { get; set; } = null!;

    public string? FileUrl { get; set; }

    public string? MimeType { get; set; }

    public long FileSizeBytes { get; set; }

    public string? ChecksumSha256 { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual DeliverableVersion? DeliverableVersion { get; set; }

    public virtual Meeting? Meeting { get; set; }

    public virtual ProgressReport? ProgressReport { get; set; }

    public virtual SupervisorFeedback? SupervisorFeedback { get; set; }

    public virtual User UploadedByNavigation { get; set; } = null!;
}
