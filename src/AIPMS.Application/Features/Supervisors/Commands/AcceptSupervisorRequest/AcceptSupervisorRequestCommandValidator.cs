using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Commands.AcceptSupervisorRequest;

public sealed class AcceptSupervisorRequestCommandValidator : AbstractValidator<AcceptSupervisorRequestCommand>
{
    public AcceptSupervisorRequestCommandValidator() => RuleFor(x => x.Id).GreaterThan(0);
}
