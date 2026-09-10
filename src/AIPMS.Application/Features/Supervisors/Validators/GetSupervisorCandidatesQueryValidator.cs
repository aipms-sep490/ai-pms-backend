using AIPMS.Application.Features.Supervisors.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Validators;

public sealed class GetSupervisorCandidatesQueryValidator : AbstractValidator<GetSupervisorCandidatesQuery>
{
    public GetSupervisorCandidatesQueryValidator()
    {
        RuleFor(q => q.ProjectId).GreaterThan(0);
        RuleFor(q => q.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.Search).MaximumLength(255);
        RuleFor(q => q.Expertise).MaximumLength(255);
    }
}
