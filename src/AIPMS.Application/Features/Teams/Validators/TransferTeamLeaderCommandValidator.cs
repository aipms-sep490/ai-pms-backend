using AIPMS.Application.Features.Teams.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class TransferTeamLeaderCommandValidator : AbstractValidator<TransferTeamLeaderCommand>
{
    public TransferTeamLeaderCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.NewLeaderUserId).GreaterThan(0);
    }
}
