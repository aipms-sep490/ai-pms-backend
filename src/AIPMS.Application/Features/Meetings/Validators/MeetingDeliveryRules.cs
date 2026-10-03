namespace AIPMS.Application.Features.Meetings.Validators;

public static class MeetingDeliveryRules
{
    public static (string Mode, string Channel) Legacy(string? location, string? url) =>
        (string.IsNullOrWhiteSpace(url) ? "ONSITE" : string.IsNullOrWhiteSpace(location) ? "REMOTE" : "HYBRID",
         string.IsNullOrWhiteSpace(url) ? "NONE" : "EXTERNAL_LINK");

    public static bool IsValid(string? mode, string? channel, string? location, string? onlineUrl)
    {
        // Both absent identifies the pre-video contract. Explicit metadata always uses the strict rules.
        if (mode is null && channel is null) return true;
        if ((mode, channel) is not (("ONSITE", "NONE") or ("REMOTE", "EXTERNAL_LINK") or
            ("REMOTE", "IN_APP_VIDEO") or ("HYBRID", "EXTERNAL_LINK") or ("HYBRID", "IN_APP_VIDEO"))) return false;
        if (mode is "ONSITE" or "HYBRID" && string.IsNullOrWhiteSpace(location)) return false;
        if (channel != "EXTERNAL_LINK") return string.IsNullOrWhiteSpace(onlineUrl);
        return Uri.TryCreate(onlineUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    }
}
