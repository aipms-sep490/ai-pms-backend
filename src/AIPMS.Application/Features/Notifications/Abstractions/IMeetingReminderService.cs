namespace AIPMS.Application.Features.Notifications.Abstractions;

public interface IMeetingReminderService
{
    const string NotificationType = "MEETING_START_REMINDER";
    Task<IReadOnlyList<long>> GetDueMeetingIdsAsync(long afterId, int limit, DateTime nowUtc, int minutesBefore, CancellationToken ct);
    Task EnqueueAsync(long meetingId, DateTime nowUtc, int minutesBefore, CancellationToken ct);
}
