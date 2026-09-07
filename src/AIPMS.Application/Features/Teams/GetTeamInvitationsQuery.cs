using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record GetTeamInvitationsQuery(long? TeamId = null, int Page = 1, int PageSize = 20) : IRequest<PagedResult<TeamInvitationData>>;

public sealed class GetTeamInvitationsQueryHandler(TeamWorkflow workflow)
    : IRequestHandler<GetTeamInvitationsQuery, PagedResult<TeamInvitationData>>
{
    public Task<PagedResult<TeamInvitationData>> Handle(GetTeamInvitationsQuery request, CancellationToken cancellationToken) =>
        workflow.InvitationsAsync(request.TeamId, request.Page, request.PageSize, cancellationToken);
}

public sealed class GetTeamInvitationsQueryValidator : AbstractValidator<GetTeamInvitationsQuery>
{
    public GetTeamInvitationsQueryValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0).When(x => x.TeamId.HasValue);
        RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
