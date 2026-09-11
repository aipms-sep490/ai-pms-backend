using AIPMS.Application.Features.Tasks.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class GetTaskStatusHistoryQueryValidator : AbstractValidator<GetTaskStatusHistoryQuery>
{
    public GetTaskStatusHistoryQueryValidator()
    {
        RuleFor(static x => x.TaskId)
            .GreaterThan(0).WithMessage("Task ID must be greater than 0.");
    }
}
