using AIPMS.Application.Features.Teams.Commands;
using AIPMS.Application.Features.Teams.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class CheckTeamEligibilityCommandValidator : AbstractValidator<CheckTeamEligibilityCommand>
{
    public CheckTeamEligibilityCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
    }
}

public sealed class LockTeamEligibilityCommandValidator : AbstractValidator<LockTeamEligibilityCommand>
{
    public LockTeamEligibilityCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
    }
}

public sealed class GetTeamEligibilityQueryValidator : AbstractValidator<GetTeamEligibilityQuery>
{
    public GetTeamEligibilityQueryValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
    }
}

public sealed class GetTeamEligibilityHistoryQueryValidator : AbstractValidator<GetTeamEligibilityHistoryQuery>
{
    public GetTeamEligibilityHistoryQueryValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
    }
}
