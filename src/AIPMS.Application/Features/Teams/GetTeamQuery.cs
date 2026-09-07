using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record GetTeamQuery(long TeamId) : IRequest<TeamDto>;

public sealed class GetTeamQueryHandler(TeamWorkflow workflow)
    : IRequestHandler<GetTeamQuery, TeamDto>
{
    public Task<TeamDto> Handle(GetTeamQuery request, CancellationToken cancellationToken) =>
        workflow.GetAsync(request.TeamId, cancellationToken);
}

public sealed class GetTeamQueryValidator : AbstractValidator<GetTeamQuery>
{
    public GetTeamQueryValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
    }
}
