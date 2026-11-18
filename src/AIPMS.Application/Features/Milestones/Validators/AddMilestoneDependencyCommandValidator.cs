using AIPMS.Application.Features.Milestones.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Milestones.Validators;

public sealed class AddMilestoneDependencyCommandValidator : AbstractValidator<AddMilestoneDependencyCommand>
{
    private static readonly string[] AllowedTypes = ["FINISH_TO_START"];

    public AddMilestoneDependencyCommandValidator()
    {
        RuleFor(static x => x.MilestoneId)
            .GreaterThan(0).WithMessage("MilestoneId must be greater than 0.");

        RuleFor(static x => x.DependsOnMilestoneId)
            .GreaterThan(0).WithMessage("DependsOnMilestoneId must be greater than 0.");

        RuleFor(static x => x.DependencyType)
            .NotEmpty().WithMessage("DependencyType is required.")
            .Must(static t => AllowedTypes.Contains(t))
            .WithMessage("Only FINISH_TO_START dependency type is currently supported by business policy.");

        RuleFor(static x => x)
            .Must(static x => x.MilestoneId != x.DependsOnMilestoneId)
            .WithMessage("Milestone cannot depend on itself.");
    }
}
