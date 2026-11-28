using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Notifications.Queries;

public sealed record GetEmailDeliverySummaryQuery : IRequest<EmailDeliverySummaryDto>;

public sealed class GetEmailDeliverySummaryQueryHandler(
    INotificationEmailAdminService service) : IRequestHandler<GetEmailDeliverySummaryQuery, EmailDeliverySummaryDto>
{
    public Task<EmailDeliverySummaryDto> Handle(GetEmailDeliverySummaryQuery request, CancellationToken cancellationToken) =>
        service.GetSummaryAsync(cancellationToken);
}
