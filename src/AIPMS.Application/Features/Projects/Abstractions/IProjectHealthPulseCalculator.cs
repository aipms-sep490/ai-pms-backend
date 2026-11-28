using AIPMS.Application.Features.ProjectHub;

namespace AIPMS.Application.Features.Projects.Abstractions;

/// <summary>
/// Single authoritative 4-signal Health Pulse calculator (Spec V4 line 279 / TK-W2-13)
/// shared by ProjectHub, Dashboards, ProgressRiskEnricher, and RiskAlertWorker.
/// </summary>
public interface IProjectHealthPulseCalculator
{
    Task<ProjectHealthPulseDto> CalculateAsync(long projectId, DateTime nowUtc, CancellationToken ct);

    Task<IReadOnlyDictionary<long, ProjectHealthPulseDto>> CalculateBatchAsync(
        IReadOnlyList<long> projectIds,
        DateTime nowUtc,
        CancellationToken ct);
}

public static class HealthPulseMapping
{
    public static string ToRiskLevel(string healthPulseStatus) => healthPulseStatus switch
    {
        "GREEN" => "LOW",
        "YELLOW" => "MEDIUM",
        "RED" => "HIGH",
        _ => "INSUFFICIENT_DATA"
    };

    public static string ToDataStatus(string healthPulseStatus) =>
        string.Equals(healthPulseStatus, "INSUFFICIENT_DATA", StringComparison.OrdinalIgnoreCase)
            ? "INSUFFICIENT_DATA"
            : "SUFFICIENT";
}
