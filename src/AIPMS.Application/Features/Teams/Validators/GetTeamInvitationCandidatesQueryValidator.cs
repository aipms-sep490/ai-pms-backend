using AIPMS.Application.Features.Teams.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class GetTeamInvitationCandidatesQueryValidator : AbstractValidator<GetTeamInvitationCandidatesQuery>
{
    public GetTeamInvitationCandidatesQueryValidator()
    {
        RuleFor(r => r.TeamId).GreaterThan(0);
        RuleFor(r => r.Search).MaximumLength(255);
        RuleFor(r => r.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
