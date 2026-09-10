using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record CreateTeamCommand(long AcademicSemesterId, string Code, string Name, string? Description) : IRequest<TeamDto>;

public sealed class CreateTeamCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<CreateTeamCommand, TeamDto>
{
    public Task<TeamDto> Handle(CreateTeamCommand request, CancellationToken cancellationToken) =>
        workflow.CreateAsync(request, cancellationToken);
}
