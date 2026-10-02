namespace AIPMS.Application.Features.Meetings.Video;

public sealed class VideoMeetingOptions
{
    public const string SectionName = "VideoMeeting";
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "LIVEKIT";
    public string ServerUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public int JoinTokenTtlSeconds { get; set; } = 300;
    public int JoinOpenBeforeMinutes { get; set; }
    public int JoinCloseAfterMinutes { get; set; }
    public bool IsReady => !Enabled || (Provider == "LIVEKIT" && Uri.TryCreate(ServerUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret) && JoinTokenTtlSeconds is >= 60 and <= 300);
}


