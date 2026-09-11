namespace AIPMS.AI.Configuration;

/// <summary>
/// Centralized provisional configuration for deterministic rule baseline risk scoring.
/// PROVISIONAL VALUES — REQUIRES LEADER APPROVAL BEFORE PRODUCTION FINALIZATION.
/// </summary>
public static class RuleBaselineConfig
{
    public const string RuleVersion = "PROVISIONAL_RULE_BASELINE_1.0";
    public const string FeatureVersion = "FEATURE_SET_1.0";
    public const string ModelVersion = "RULE_BASED";

    // Thresholds & Lookback Windows (Provisional)
    public const int MilestoneNearDueThresholdDays = 7;
    public const int MeetingLookbackDays = 30;

    // Feature Weights (Provisional — Sum = 100.0)
    public const double OverdueWeight = 40.0;
    public const double BlockedWeight = 30.0;
    public const double MilestoneWeight = 20.0;
    public const double UnassignedWeight = 10.0;

    // Risk Level Threshold Boundaries (Provisional)
    public const double CriticalRiskScoreThreshold = 75.0;
    public const double CriticalOverdueRatioThreshold = 0.50;
    public const double CriticalBlockedRatioThreshold = 0.40;

    public const double HighRiskScoreThreshold = 50.0;
    public const double HighOverdueRatioThreshold = 0.30;
    public const double HighBlockedRatioThreshold = 0.25;

    public const double MediumRiskScoreThreshold = 25.0;
    public const double MediumOverdueRatioThreshold = 0.15;
    public const double MediumMilestoneCompletionThreshold = 0.50;
}
