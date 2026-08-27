using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Commands.RejectSupervisorRequest;

public sealed class RejectSupervisorRequestCommandValidator : AbstractValidator<RejectSupervisorRequestCommand>
{
    public RejectSupervisorRequestCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.ResponseMessage).MaximumLength(2000);
    }
}
