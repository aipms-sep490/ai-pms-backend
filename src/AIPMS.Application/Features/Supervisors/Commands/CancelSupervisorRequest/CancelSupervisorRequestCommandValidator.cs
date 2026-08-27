using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Commands.CancelSupervisorRequest;

public sealed class CancelSupervisorRequestCommandValidator : AbstractValidator<CancelSupervisorRequestCommand>
{
    public CancelSupervisorRequestCommandValidator() => RuleFor(x => x.Id).GreaterThan(0);
}
