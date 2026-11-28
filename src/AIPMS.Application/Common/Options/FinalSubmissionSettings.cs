namespace AIPMS.Application.Common.Options;

public sealed class FinalSubmissionSettings
{
    public const string SectionName = "FinalSubmissions";

    /// <summary>
    /// When false (default in Wave 3), legacy 1-step POST /final-submission is disabled in favor of
    /// the 2-step Pre-Defense lock (Leader request-lock -> Primary Supervisor LOCK). Can be flipped
    /// to true for instant config rollback without redeploying.
    /// </summary>
    public bool AllowLegacyOneStepLock { get; set; } = false;
}
