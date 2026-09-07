using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record RemoveTeamMemberCommand(long TeamId, long? MemberUserId) : IRequest<bool>;

public sealed class RemoveTeamMemberCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<RemoveTeamMemberCommand, bool>
{
    public Task<bool> Handle(RemoveTeamMemberCommand request, CancellationToken cancellationToken) =>
        workflow.RemoveAsync(request.TeamId, request.MemberUserId, cancellationToken);
}

public sealed class RemoveTeamMemberCommandValidator : AbstractValidator<RemoveTeamMemberCommand>
{
    public RemoveTeamMemberCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.MemberUserId).GreaterThan(0).When(x => x.MemberUserId.HasValue);
    }
}
