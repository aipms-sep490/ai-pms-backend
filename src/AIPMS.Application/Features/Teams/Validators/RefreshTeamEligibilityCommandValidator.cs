using AIPMS.Application.Features.Teams.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class RefreshTeamEligibilityCommandValidator : AbstractValidator<RefreshTeamEligibilityCommand>
{
    public RefreshTeamEligibilityCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
    }
}
