using AIPMS.Application.Features.Milestones.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Milestones.Validators;

public sealed class CreateMilestoneCommandValidator : AbstractValidator<CreateMilestoneCommand>
{
    public CreateMilestoneCommandValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(static x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(255).WithMessage("Title must not exceed 255 characters.");

        RuleFor(static x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be greater than or equal to 0.");

        RuleFor(static x => x)
            .Must(static x => x.DueDate == null || x.StartDate == null || x.DueDate >= x.StartDate)
            .WithMessage("DueDate must be greater than or equal to StartDate.");
    }
}
