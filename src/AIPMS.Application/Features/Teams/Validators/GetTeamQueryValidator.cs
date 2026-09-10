using AIPMS.Application.Features.Teams.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class GetTeamQueryValidator : AbstractValidator<GetTeamQuery>
{
    public GetTeamQueryValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
    }
}
