using AIPMS.Application.Features.Teams.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class RemoveTeamMemberCommandValidator : AbstractValidator<RemoveTeamMemberCommand>
{
    public RemoveTeamMemberCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.MemberUserId).GreaterThan(0).When(x => x.MemberUserId.HasValue);
    }
}
