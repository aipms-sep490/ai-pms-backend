using AIPMS.Application.Features.Tasks.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class RemoveTaskDependencyCommandValidator : AbstractValidator<RemoveTaskDependencyCommand>
{
    public RemoveTaskDependencyCommandValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("TaskId must be greater than 0.");

        RuleFor(static x => x.DependsOnTaskId)
            .GreaterThan(0).WithMessage("DependsOnTaskId must be greater than 0.");
    }
}
