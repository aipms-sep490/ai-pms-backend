using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Commands.EndSupervisorAssignment;

public sealed class EndSupervisorAssignmentCommandValidator : AbstractValidator<EndSupervisorAssignmentCommand>
{
    public EndSupervisorAssignmentCommandValidator()
    {
        RuleFor(command => command.Id)
            .GreaterThan(0)
            .WithMessage("Assignment id must be greater than 0.");
    }
}
