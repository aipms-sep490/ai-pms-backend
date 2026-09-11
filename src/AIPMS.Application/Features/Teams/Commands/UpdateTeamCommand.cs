using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record UpdateTeamCommand(long TeamId, string Name, string? Description) : IRequest<TeamDto>;

public sealed class UpdateTeamCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<UpdateTeamCommand, TeamDto>
{
    public Task<TeamDto> Handle(UpdateTeamCommand request, CancellationToken cancellationToken) =>
        workflow.UpdateAsync(request, cancellationToken);
}
