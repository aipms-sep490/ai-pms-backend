using AIPMS.Application.Features.Teams.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class AcceptTeamInvitationCommandValidator : AbstractValidator<AcceptTeamInvitationCommand>
{
    public AcceptTeamInvitationCommandValidator()
    {
        RuleFor(x => x.InvitationId).GreaterThan(0);
    }
}
