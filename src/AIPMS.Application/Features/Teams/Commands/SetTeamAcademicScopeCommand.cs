using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record SetTeamAcademicScopeCommand(long TeamId, TeamAcademicScopeRequest Scope) : IRequest<TeamDto>;

public sealed class SetTeamAcademicScopeCommandHandler(TeamWorkflow workflow) : IRequestHandler<SetTeamAcademicScopeCommand, TeamDto>
{
    public Task<TeamDto> Handle(SetTeamAcademicScopeCommand request, CancellationToken ct) => workflow.SetScopeAsync(request, ct);
}

