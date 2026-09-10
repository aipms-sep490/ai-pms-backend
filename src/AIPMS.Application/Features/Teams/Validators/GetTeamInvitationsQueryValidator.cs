using AIPMS.Application.Features.Teams.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class GetTeamInvitationsQueryValidator : AbstractValidator<GetTeamInvitationsQuery>
{
    public GetTeamInvitationsQueryValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0).When(x => x.TeamId.HasValue);
        RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
