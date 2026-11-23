using System;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class MeetingTranscriptSegment
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public long? MeetingVideoSessionId { get; set; }
    public long? SpeakerUserId { get; set; }
    public string SpeakerName { get; set; } = string.Empty;
    public string Language { get; set; } = "vi-VN";
    public string Source { get; set; } = "WEB_SPEECH";
    public string Content { get; set; } = string.Empty;
    public bool IsHighlighted { get; set; }
    public string? ClientSegmentId { get; set; }
    public DateTime SpokenAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Meeting Meeting { get; set; } = null!;
}
