using AIPMS.Application.Features.Supervisors.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Validators;

public sealed class ReplaceSupervisorAssignmentCommandValidator : AbstractValidator<ReplaceSupervisorAssignmentCommand>
{
    public ReplaceSupervisorAssignmentCommandValidator()
    {
        RuleFor(x => x.AssignmentId).GreaterThan(0);
        RuleFor(x => x.SupervisorProfileId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000).Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
