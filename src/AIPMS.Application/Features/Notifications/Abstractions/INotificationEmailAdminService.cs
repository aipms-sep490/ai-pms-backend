using AIPMS.Application.Features.Notifications.DTOs;

namespace AIPMS.Application.Features.Notifications.Abstractions;

public interface INotificationEmailAdminService
{
    Task<EmailDeliverySummaryDto> GetSummaryAsync(CancellationToken ct = default);
    Task<RetryFailedEmailsResultDto> RetryFailedAsync(CancellationToken ct = default);
}
