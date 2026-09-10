using AIPMS.Application.Features.Tasks.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class SetTaskAssigneesCommandValidator : AbstractValidator<SetTaskAssigneesCommand>
{
    public SetTaskAssigneesCommandValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("Task ID must be greater than 0.");

        RuleFor(static x => x.AssigneeUserIds)
            .NotNull().WithMessage("AssigneeUserIds cannot be null.")
            .Must(static ids => ids == null || ids.Distinct().Count() == ids.Count)
            .WithMessage("AssigneeUserIds must not contain duplicate values.");
    }
}
