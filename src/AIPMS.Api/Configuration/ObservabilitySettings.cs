using System.ComponentModel.DataAnnotations;
using Serilog.Events;

namespace AIPMS.Api.Configuration;

public sealed class ObservabilitySettings
{
    public const string SectionName = "Observability";

    [Required]
    public string ApplicationName { get; init; } = "AI-PMS API";

    [Required]
    public string LogFilePath { get; init; } = "logs/aipms-.log";

    [Range(1, 90)]
    public int RetainedFileCountLimit { get; init; } = 14;

    public LogEventLevel MinimumLevel { get; init; } = LogEventLevel.Information;
}
