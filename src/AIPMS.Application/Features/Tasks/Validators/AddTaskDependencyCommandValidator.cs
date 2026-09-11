using AIPMS.Application.Features.Tasks.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class AddTaskDependencyCommandValidator : AbstractValidator<AddTaskDependencyCommand>
{
    private static readonly string[] AllowedTypes = ["FINISH_TO_START", "START_TO_START", "FINISH_TO_FINISH", "START_TO_FINISH"];

    public AddTaskDependencyCommandValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("TaskId must be greater than 0.");

        RuleFor(static x => x.DependsOnTaskId)
            .GreaterThan(0).WithMessage("DependsOnTaskId must be greater than 0.");

        RuleFor(static x => x.DependencyType)
            .NotEmpty().WithMessage("DependencyType is required.")
            .Must(static t => AllowedTypes.Contains(t))
            .WithMessage($"DependencyType must be one of: {string.Join(", ", AllowedTypes)}.");

        RuleFor(static x => x)
            .Must(static x => x.TaskId != x.DependsOnTaskId)
            .WithMessage("Task cannot depend on itself.");
    }
}
