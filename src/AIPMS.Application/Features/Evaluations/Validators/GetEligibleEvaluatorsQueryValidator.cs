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
        RuleFor(r => r.ComponentId).GreaterThan(0).When(r => r.ComponentId.HasValue);
        RuleFor(r => r.Scope).Must(s => s is null or "COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL");
        RuleFor(r => r.MajorId).GreaterThan(0).When(r => r.MajorId.HasValue);
        RuleFor(r => r.StudentId).GreaterThan(0).When(r => r.StudentId.HasValue);
        RuleFor(r => r.ComponentId).NotNull().When(r => r.Scope is not null || r.MajorId.HasValue || r.StudentId.HasValue);
    }
}
