using AIPMS.Application.Features.Teams.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class GetCurrentTeamQueryValidator : AbstractValidator<GetCurrentTeamQuery>
{
    public GetCurrentTeamQueryValidator()
    {
        RuleFor(x => x.AcademicSemesterId).GreaterThan(0);
    }
}
