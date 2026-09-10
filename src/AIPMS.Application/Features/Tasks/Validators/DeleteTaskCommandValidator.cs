using AIPMS.Application.Features.Tasks.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class DeleteTaskCommandValidator : AbstractValidator<DeleteTaskCommand>
{
    public DeleteTaskCommandValidator()
    {
        RuleFor(static x => x.Id)
            .GreaterThan(0).WithMessage("Task ID must be greater than 0.");
    }
}
