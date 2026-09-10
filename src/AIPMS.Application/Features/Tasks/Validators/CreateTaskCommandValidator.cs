using AIPMS.Application.Features.Tasks.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    private static readonly string[] AllowedPriorities = ["LOW", "MEDIUM", "HIGH", "CRITICAL"];

    public CreateTaskCommandValidator()
    {
        RuleFor(static x => x.MilestoneId)
            .GreaterThan(0).WithMessage("MilestoneId must be greater than 0.");

        RuleFor(static x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(255).WithMessage("Title must not exceed 255 characters.");

        RuleFor(static x => x.Priority)
            .Must(static p => p == null || AllowedPriorities.Contains(p))
            .WithMessage($"Priority must be one of: {string.Join(", ", AllowedPriorities)}.");

        RuleFor(static x => x)
            .Must(static x => x.DueAt == null || x.StartAt == null || x.DueAt >= x.StartAt)
            .WithMessage("DueAt must be greater than or equal to StartAt.");

        RuleFor(static x => x.AssigneeUserIds)
            .NotNull().WithMessage("AssigneeUserIds cannot be null.")
            .Must(static ids => ids == null || ids.Distinct().Count() == ids.Count)
            .WithMessage("AssigneeUserIds must not contain duplicate values.");
    }
}
