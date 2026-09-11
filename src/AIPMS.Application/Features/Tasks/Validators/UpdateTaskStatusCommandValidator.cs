using AIPMS.Application.Features.Tasks.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class UpdateTaskStatusCommandValidator : AbstractValidator<UpdateTaskStatusCommand>
{
    private static readonly string[] AllowedStatuses = ["TODO", "IN_PROGRESS", "BLOCKED", "IN_REVIEW", "DONE", "CANCELLED"];

    public UpdateTaskStatusCommandValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("Task ID must be greater than 0.");

        RuleFor(static x => x.NewStatus)
            .NotEmpty().WithMessage("NewStatus is required.")
            .Must(static s => AllowedStatuses.Contains(s))
            .WithMessage($"NewStatus must be one of: {string.Join(", ", AllowedStatuses)}.");

        RuleFor(static x => x.Reason)
            .MaximumLength(1000).WithMessage("Reason must not exceed 1000 characters.");

        // Blocker reason required
        RuleFor(static x => x)
            .Must(static x => x.NewStatus != "BLOCKED" || !string.IsNullOrWhiteSpace(x.Reason))
            .WithName("Reason")
            .WithMessage("A reason is required when transition status to BLOCKED.");
    }
}
