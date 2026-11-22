using System;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class MeetingAiInsight
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public long? MeetingVideoSessionId { get; set; }
    public string InsightType { get; set; } = "FULL_MINUTES";
    public string Status { get; set; } = "COMPLETED";
    public string? ModelVersion { get; set; }
    public int TranscriptSegmentCount { get; set; }
    public long? LastSegmentId { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Meeting Meeting { get; set; } = null!;
}
