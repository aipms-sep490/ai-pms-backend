namespace AIPMS.Application.Features.Notifications.Abstractions;

public interface IDepartmentDailyDigestService
{
    Task<int> SendDigestAsync(DateTime utcNow, CancellationToken ct = default);
}
