using AIPMS.Application.Features.Notifications.Commands;
using AIPMS.Application.Features.Notifications.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Notifications.Validators;

public sealed class GetNotificationsQueryValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsQueryValidator()
    {
        RuleFor(r => r.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
        RuleFor(r => r.NotificationType).MaximumLength(50);
    }
}

public sealed class MarkNotificationReadCommandValidator : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadCommandValidator() => RuleFor(r => r.NotificationId).GreaterThan(0);
}
