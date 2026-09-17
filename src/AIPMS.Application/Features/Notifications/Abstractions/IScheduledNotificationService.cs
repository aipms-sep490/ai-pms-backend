namespace AIPMS.Application.Features.Notifications.Abstractions;

public interface IScheduledNotificationService
{
    Task<IReadOnlyList<long>> GetProjectIdsAsync(long afterId, int limit, CancellationToken ct);
    Task ProcessProjectAsync(long projectId, DateTime nowUtc, TimeSpan reminderWindow, CancellationToken ct);
}
