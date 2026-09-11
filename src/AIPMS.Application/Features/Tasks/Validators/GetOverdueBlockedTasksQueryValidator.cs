using AIPMS.Application.Features.Tasks.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class GetOverdueBlockedTasksQueryValidator : AbstractValidator<GetOverdueBlockedTasksQuery>
{
    public GetOverdueBlockedTasksQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}
