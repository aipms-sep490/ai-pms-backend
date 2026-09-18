using System.ComponentModel.DataAnnotations;

namespace AIPMS.Api.Configuration;

public sealed class NotificationEmailSettings
{
    public const string SectionName = "NotificationEmail";
    public bool Enabled { get; set; }
    [Range(15, 86400)] public int IntervalSeconds { get; set; } = 60;
    [Range(1, 100)] public int BatchSize { get; set; } = 20;
    [Range(1, 20)] public int MaxAttempts { get; set; } = 8;
}
