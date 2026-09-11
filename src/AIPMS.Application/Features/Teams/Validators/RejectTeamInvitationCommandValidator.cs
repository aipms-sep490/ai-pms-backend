using AIPMS.Application.Features.Teams.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class RejectTeamInvitationCommandValidator : AbstractValidator<RejectTeamInvitationCommand>
{
    public RejectTeamInvitationCommandValidator()
    {
        RuleFor(x => x.InvitationId).GreaterThan(0);
    }
}
