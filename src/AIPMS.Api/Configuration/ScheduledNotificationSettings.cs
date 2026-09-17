using System.ComponentModel.DataAnnotations;

namespace AIPMS.Api.Configuration;

public sealed class ScheduledNotificationSettings
{
    public const string SectionName = "ScheduledNotifications";
    public bool Enabled { get; set; }
    [Range(30, 86400)] public int IntervalSeconds { get; set; } = 300;
    [Range(1, 720)] public int ReminderHours { get; set; } = 24;
    [Range(1, 500)] public int BatchSize { get; set; } = 50;
}
