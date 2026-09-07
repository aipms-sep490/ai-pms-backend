using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record GetCurrentTeamQuery(long AcademicSemesterId) : IRequest<TeamDto?>;

public sealed class GetCurrentTeamQueryHandler(TeamWorkflow workflow)
    : IRequestHandler<GetCurrentTeamQuery, TeamDto?>
{
    public Task<TeamDto?> Handle(GetCurrentTeamQuery request, CancellationToken cancellationToken) =>
        workflow.CurrentAsync(request.AcademicSemesterId, cancellationToken);
}

public sealed class GetCurrentTeamQueryValidator : AbstractValidator<GetCurrentTeamQuery>
{
    public GetCurrentTeamQueryValidator()
    {
        RuleFor(x => x.AcademicSemesterId).GreaterThan(0);
    }
}
