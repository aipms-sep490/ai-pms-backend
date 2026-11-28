using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Notifications.Commands;

public sealed record RetryFailedEmailDeliveriesCommand : IRequest<RetryFailedEmailsResultDto>;

public sealed class RetryFailedEmailDeliveriesCommandHandler(
    INotificationEmailAdminService service) : IRequestHandler<RetryFailedEmailDeliveriesCommand, RetryFailedEmailsResultDto>
{
    public Task<RetryFailedEmailsResultDto> Handle(RetryFailedEmailDeliveriesCommand request, CancellationToken cancellationToken) =>
        service.RetryFailedAsync(cancellationToken);
}
