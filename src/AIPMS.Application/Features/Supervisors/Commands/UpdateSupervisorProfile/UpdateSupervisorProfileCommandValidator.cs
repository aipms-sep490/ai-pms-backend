using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Commands.UpdateSupervisorProfile;

public sealed class UpdateSupervisorProfileCommandValidator : AbstractValidator<UpdateSupervisorProfileCommand>
{
    public UpdateSupervisorProfileCommandValidator()
    {
        RuleFor(command => command.MaxActiveProjects)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Max active projects cannot be negative.")
            .When(command => command.MaxActiveProjects.HasValue);
    }
}
