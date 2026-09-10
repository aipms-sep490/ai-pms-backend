using AIPMS.Application.Features.Tasks.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class GetTaskByIdQueryValidator : AbstractValidator<GetTaskByIdQuery>
{
    public GetTaskByIdQueryValidator()
    {
        RuleFor(static x => x.Id)
            .GreaterThan(0).WithMessage("Task ID must be greater than 0.");
    }
}
