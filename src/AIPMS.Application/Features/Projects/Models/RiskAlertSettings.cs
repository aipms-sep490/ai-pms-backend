using System.ComponentModel.DataAnnotations;

namespace AIPMS.Application.Features.Projects.Models;

public sealed class RiskAlertSettings
{
    public const string SectionName = "RiskAlert";

    public bool Enabled { get; set; } = false;

    [Range(30, 86400)]
    public int IntervalSeconds { get; set; } = 3600;

    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    [Range(1, 100)]
    public int OverdueTaskThreshold { get; set; } = 3;

    [Range(1, 100)]
    public int BlockedTaskThreshold { get; set; } = 2;

    [Range(0.0, 100.0)]
    public double CriticalRiskScoreThreshold { get; set; } = 50.0;
}
