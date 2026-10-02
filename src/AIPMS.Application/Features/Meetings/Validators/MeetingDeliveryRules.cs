namespace AIPMS.Application.Features.Meetings.Validators;

public static class MeetingDeliveryRules
{
    public static bool IsValid(string mode, string channel, string? location, string? onlineUrl)
    {
        if (mode is not ("ONSITE" or "REMOTE" or "HYBRID") || channel is not ("NONE" or "EXTERNAL_LINK" or "IN_APP_VIDEO")) return false;
        if (mode is "ONSITE" or "HYBRID" && string.IsNullOrWhiteSpace(location)) return false;
        if (mode == "ONSITE" && channel != "NONE") return false;
        if (channel == "NONE" && !string.IsNullOrWhiteSpace(onlineUrl)) return false;
        if (channel == "EXTERNAL_LINK" && (string.IsNullOrWhiteSpace(onlineUrl) || !Uri.TryCreate(onlineUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)) return false;
        if (channel == "IN_APP_VIDEO" && !string.IsNullOrWhiteSpace(onlineUrl)) return false;
        return true;
    }
}


