namespace AIPMS.Application.Common.Options;

public sealed class ExecutionConcurrencySettings
{
    public const string SectionName = "ExecutionConcurrency";

    public bool RequireTokens { get; set; }
    public bool LogMissingTokens { get; set; } = true;
}
