using System.Text.Json;
using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Evaluations.Services;

public static class ResultSnapshotReader
{
    public static ResultBreakdownDto Legacy(string calculationVersion) => new(null, null, calculationVersion,
        true, null, "UNKNOWN", "LEGACY", null, null, null, [], []);

    // Only our versioned, typed projection is public; the audit snapshot contains private rosters and comments.
    public static ResultBreakdownDto Read(string? json, string calculationVersion, long? studentId = null, long? majorId = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return Legacy(calculationVersion);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("SnapshotVersion", out var version) || version.GetInt32() != 2
                || !root.TryGetProperty("Breakdown", out var value)) return Legacy(calculationVersion);
            if (studentId.HasValue && (!root.TryGetProperty("Target", out var target) || target.ValueKind != JsonValueKind.Object
                || target.GetProperty("StudentId").GetInt64() != studentId || target.GetProperty("MajorId").GetInt64() != majorId))
                return Legacy(calculationVersion);
            var result = value.Deserialize<ResultBreakdownDto>();
            if (result is null || result.Components is null || result.MajorBreakdown is null) return Legacy(calculationVersion);
            var components = result.Components.Where(c => c.Scope == "COMMON" || (studentId.HasValue
                ? c.MajorId == majorId && c.Scope is "MAJOR_SPECIFIC" or "INDIVIDUAL" : c.Scope == "MAJOR_SPECIFIC")).ToArray();
            return result with { Components = components,
                MajorBreakdown = result.MajorBreakdown.Where(m => !studentId.HasValue || m.MajorId == majorId).ToArray() };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return Legacy(calculationVersion); }
    }

    public static long? StudentDepartment(string json, long studentId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("Target", out var target) && target.ValueKind == JsonValueKind.Object
                && target.GetProperty("StudentId").GetInt64() == studentId)
                return target.GetProperty("DepartmentId").GetInt64();
            // Older publication snapshots use the lower-case property name.
            if (document.RootElement.TryGetProperty("target", out target) && target.ValueKind == JsonValueKind.Object
                && target.GetProperty("StudentId").GetInt64() == studentId)
                return target.GetProperty("DepartmentId").GetInt64();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { }
        return null;
    }
}
