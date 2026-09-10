using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Queries;

public sealed record GetCurrentTeamQuery(long AcademicSemesterId) : IRequest<TeamDto?>;

public sealed class GetCurrentTeamQueryHandler(TeamWorkflow workflow)
    : IRequestHandler<GetCurrentTeamQuery, TeamDto?>
{
    public Task<TeamDto?> Handle(GetCurrentTeamQuery request, CancellationToken cancellationToken) =>
        workflow.CurrentAsync(request.AcademicSemesterId, cancellationToken);
}
