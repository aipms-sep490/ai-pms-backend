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
    public bool IsReady => !Enabled || (Provider == "LIVEKIT" && Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "wss" && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret) && JoinTokenTtlSeconds is >= 1 and <= 300 && JoinOpenBeforeMinutes is >= 0 and <= 1440 && JoinCloseAfterMinutes is >= 0 and <= 1440);
}


