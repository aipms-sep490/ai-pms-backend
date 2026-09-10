using AIPMS.Application.Features.Projects.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Projects.Validators;

public sealed class GetTimelineDataQueryValidator : AbstractValidator<GetTimelineDataQuery>
{
    public GetTimelineDataQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}
