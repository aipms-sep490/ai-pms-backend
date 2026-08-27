using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Commands.SendSupervisorRequest;

public sealed class SendSupervisorRequestCommandValidator : AbstractValidator<SendSupervisorRequestCommand>
{
    public SendSupervisorRequestCommandValidator()
    {
        RuleFor(command => command.ProjectId)
            .GreaterThan(0)
            .WithMessage("Project id must be greater than 0.");

        RuleFor(command => command.SupervisorId)
            .GreaterThan(0)
            .WithMessage("Supervisor id must be greater than 0.");

        RuleFor(command => command.RequestMessage).MaximumLength(2000);
    }
}
