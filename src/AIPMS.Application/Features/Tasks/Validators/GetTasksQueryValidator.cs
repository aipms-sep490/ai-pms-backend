using AIPMS.Application.Features.Tasks.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class GetTasksQueryValidator : AbstractValidator<GetTasksQuery>
{
    public GetTasksQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(static x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be greater than or equal to 1.");

        RuleFor(static x => x.PageSize)
            .GreaterThanOrEqualTo(1).WithMessage("PageSize must be greater than or equal to 1.")
            .LessThanOrEqualTo(100).WithMessage("PageSize must not exceed 100.");
    }
}
