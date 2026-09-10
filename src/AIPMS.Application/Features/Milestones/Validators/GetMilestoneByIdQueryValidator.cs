using AIPMS.Application.Features.Milestones.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Milestones.Validators;

public sealed class GetMilestoneByIdQueryValidator : AbstractValidator<GetMilestoneByIdQuery>
{
    public GetMilestoneByIdQueryValidator()
    {
        RuleFor(static x => x.Id)
            .GreaterThan(0).WithMessage("Milestone ID must be greater than 0.");
    }
}
