using AIPMS.Application.Features.Milestones.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Milestones.Validators;

public sealed class DeleteMilestoneCommandValidator : AbstractValidator<DeleteMilestoneCommand>
{
    public DeleteMilestoneCommandValidator()
    {
        RuleFor(static x => x.Id)
            .GreaterThan(0).WithMessage("Milestone ID must be greater than 0.");
    }
}
