using AIPMS.Application.Features.Milestones.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Milestones.Validators;

public sealed class UpdateMilestoneCommandValidator : AbstractValidator<UpdateMilestoneCommand>
{
    private static readonly string[] AllowedStatuses = ["PLANNED", "IN_PROGRESS", "COMPLETED", "CANCELLED"];

    public UpdateMilestoneCommandValidator()
    {
        RuleFor(static x => x.Id)
            .GreaterThan(0).WithMessage("Milestone ID must be greater than 0.");

        RuleFor(static x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(255).WithMessage("Title must not exceed 255 characters.");

        RuleFor(static x => x.Status)
            .NotEmpty().WithMessage("Status is required.")
            .Must(static s => AllowedStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");

        RuleFor(static x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be greater than or equal to 0.");

        RuleFor(static x => x)
            .Must(static x => x.DueDate == null || x.StartDate == null || x.DueDate >= x.StartDate)
            .WithMessage("DueDate must be greater than or equal to StartDate.");
    }
}
