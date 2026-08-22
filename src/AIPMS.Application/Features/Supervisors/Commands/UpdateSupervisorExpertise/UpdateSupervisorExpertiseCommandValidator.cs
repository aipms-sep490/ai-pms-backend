using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Commands.UpdateSupervisorExpertise;

public sealed class UpdateSupervisorExpertiseCommandValidator : AbstractValidator<UpdateSupervisorExpertiseCommand>
{
    public UpdateSupervisorExpertiseCommandValidator()
    {
        RuleForEach(command => command.Expertises)
            .ChildRules(expertise =>
            {
                expertise.RuleFor(e => e.ExpertiseName)
                    .NotEmpty()
                    .WithMessage("Expertise name is required.");
            });
    }
}
