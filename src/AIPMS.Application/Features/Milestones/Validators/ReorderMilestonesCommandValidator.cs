using AIPMS.Application.Features.Milestones.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Milestones.Validators;

public sealed class ReorderMilestonesCommandValidator : AbstractValidator<ReorderMilestonesCommand>
{
    public ReorderMilestonesCommandValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(static x => x.Items)
            .NotEmpty().WithMessage("Reorder items cannot be empty.")
            .Must(static items => items != null && items.Select(static i => i.MilestoneId).Distinct().Count() == items.Count)
            .WithMessage("Milestone IDs in reorder items must be unique.")
            .Must(static items => items != null && items.All(static i => i.SortOrder >= 0))
            .WithMessage("SortOrder cannot be negative.")
            .Must(static items => items != null && items.Select(static i => i.SortOrder).Distinct().Count() == items.Count)
            .WithMessage("SortOrder values in reorder items must be unique.");
    }
}
