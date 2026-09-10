using AIPMS.Application.Features.Teams.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class CancelTeamInvitationCommandValidator : AbstractValidator<CancelTeamInvitationCommand>
{
    public CancelTeamInvitationCommandValidator()
    {
        RuleFor(x => x.InvitationId).GreaterThan(0);
    }
}
