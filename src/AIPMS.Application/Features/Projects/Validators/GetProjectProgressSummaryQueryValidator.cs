using AIPMS.Application.Features.Projects.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Projects.Validators;

public sealed class GetProjectProgressSummaryQueryValidator : AbstractValidator<GetProjectProgressSummaryQuery>
{
    public GetProjectProgressSummaryQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}
