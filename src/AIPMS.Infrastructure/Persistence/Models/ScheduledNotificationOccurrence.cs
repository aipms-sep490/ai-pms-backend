using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ScheduledNotificationOccurrence
{
    public long ProjectId { get; set; }
    public string OccurrenceKey { get; set; } = "";
    public long NotificationId { get; set; }
    public Notification Notification { get; set; } = null!;
}
