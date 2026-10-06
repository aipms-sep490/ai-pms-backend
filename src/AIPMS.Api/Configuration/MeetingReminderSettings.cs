using System.ComponentModel.DataAnnotations;

namespace AIPMS.Api.Configuration;

public sealed class MeetingReminderSettings
{
    public const string SectionName = "MeetingReminders";
    public bool Enabled { get; set; }
    [Range(1, 1440)] public int MinutesBefore { get; set; } = 15;
    [Range(15, 300)] public int IntervalSeconds { get; set; } = 30;
    [Range(1, 500)] public int BatchSize { get; set; } = 50;
    [Range(1, 10)] public int MaxAttempts { get; set; } = 5;
}
