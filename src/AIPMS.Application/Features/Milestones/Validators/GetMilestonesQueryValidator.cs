using AIPMS.Application.Features.Milestones.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Milestones.Validators;

public sealed class GetMilestonesQueryValidator : AbstractValidator<GetMilestonesQuery>
{
    public GetMilestonesQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}
