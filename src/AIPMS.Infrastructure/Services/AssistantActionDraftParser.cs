using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIPMS.Application.Features.AcademicKnowledge;

namespace AIPMS.Infrastructure.Services;

internal static class AssistantActionDraftParser
{
    public static AssistantDraftDto? Parse(JsonElement value, string expected)
    {
        if (value.ValueKind != JsonValueKind.Object || Text(value, "kind", 32) != expected) return null;
        var title = Text(value, "title", 255);
        var description = Text(value, "description", 4000);
        if (string.IsNullOrWhiteSpace(title) || description is null) return null;
        AssistantTaskDraftDto? task = null;
        AssistantReportDraftDto? report = null;
        List<AssistantDraftDto>? tasks = null;
        if (expected == "TASK" && Object(value, "task", out var t))
        {
            var names = new List<string>();
            if (t.TryGetProperty("assigneeNames", out var a) && a.ValueKind == JsonValueKind.Array)
                names.AddRange(a.EnumerateArray().Take(30).Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!.Trim()).Where(x => x.Length is > 0 and <= 255).Distinct(StringComparer.OrdinalIgnoreCase));
            var priority = Text(t, "priority", 16);
            task = new(Instant(Text(t, "dueAt", 64)), priority is "LOW" or "MEDIUM" or "HIGH" or "CRITICAL" ? priority : null,
                Text(t, "milestoneTitle", 255), names, Text(t, "primaryMajorName", 255));
        }
        if (expected == "WEEKLY_REPORT" && Object(value, "report", out var r))
        {
            var start = Day(Text(r, "periodStart", 10));
            var end = Day(Text(r, "periodEnd", 10));
            if (start is null || end is null || string.CompareOrdinal(end, start) < 0) start = end = null;
            report = new(start, end, Text(r, "completedWork", 4000), Text(r, "inProgressWork", 4000),
                Text(r, "blockers", 4000), Text(r, "risks", 4000), Text(r, "nextActions", 4000), Text(r, "plannedWork", 4000));
        }
        if (expected == "WORK_PLAN" && value.TryGetProperty("tasks", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            tasks = [];
            foreach (var item in items.EnumerateArray().Take(8))
                if (Parse(item, "TASK") is { } child) tasks.Add(child);
        }
        return new(expected, title, description, expected == "MEETING" ? AssistantMeetingDraftParser.Parse(value) : null, task, report, tasks);
    }

    private static bool Object(JsonElement value, string key, out JsonElement child) => value.TryGetProperty(key, out child) && child.ValueKind == JsonValueKind.Object;
    private static string? Text(JsonElement value, string key, int max) => value.TryGetProperty(key, out var t)
        && t.ValueKind == JsonValueKind.String && t.GetString()?.Trim() is { } text && text.Length <= max ? text : null;
    private static string? Day(string? text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    private static DateTimeOffset? Instant(string? text) => text is not null
        && Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})$")
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.ToUniversalTime() : null;
}
