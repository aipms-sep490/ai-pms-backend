using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Queries.GetSupervisorCandidates;

public sealed class GetSupervisorCandidatesQueryValidator : AbstractValidator<GetSupervisorCandidatesQuery>
{
    public GetSupervisorCandidatesQueryValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Expertise).MaximumLength(200);
    }
}
