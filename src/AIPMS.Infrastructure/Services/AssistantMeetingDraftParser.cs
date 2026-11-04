using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIPMS.Application.Features.AcademicKnowledge;

namespace AIPMS.Infrastructure.Services;

internal static class AssistantMeetingDraftParser
{
    public static AssistantMeetingDraftDto? Parse(JsonElement draft)
    {
        if (!draft.TryGetProperty("meeting", out var meeting) || meeting.ValueKind != JsonValueKind.Object) return null;
        string? Text(string key, int max = 255) => meeting.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String && value.GetString()?.Trim() is { Length: > 0 } text && text.Length <= max ? text : null;
        DateTimeOffset? Date(string key)
        {
            var text = Text(key, 64);
            return text is not null && Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})$")
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToUniversalTime() : null;
        }
        var start = Date("startAt");
        var end = Date("endAt");
        if (start is null || end < start) end = null;
        var mode = Text("meetingDeliveryMode") is "ONSITE" or "REMOTE" or "HYBRID" ? Text("meetingDeliveryMode") : null;
        var channel = Text("videoChannel") is "NONE" or "IN_APP_VIDEO" or "EXTERNAL_LINK" ? Text("videoChannel") : null;
        if (mode == "ONSITE") channel = "NONE";
        if (mode is "REMOTE" or "HYBRID" && channel == "NONE") channel = null;
        var url = Text("onlineUrl", 2048);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) url = null;
        return new(start, end, mode, channel, mode == "REMOTE" ? null : Text("location"),
            channel == "EXTERNAL_LINK" ? url : null,
            meeting.TryGetProperty("inviteAll", out var all) && all.ValueKind == JsonValueKind.True);
    }
}
