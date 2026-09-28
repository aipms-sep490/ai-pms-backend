using AIPMS.Application.Features.Evaluations.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Evaluations.Validators;

public sealed class GetEligibleEvaluatorsQueryValidator : AbstractValidator<GetEligibleEvaluatorsQuery>
{
    public GetEligibleEvaluatorsQueryValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0);
        RuleFor(r => r.PeriodId).GreaterThan(0);
        RuleFor(r => r.Page).InclusiveBetween(1, 1000000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
