using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Domain.Entities;
using AIPMS.Domain.Enums;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries.GetProjectLifecycle;

public sealed record GetProjectLifecycleQuery : IRequest<ProjectLifecycleDto>;

public sealed class GetProjectLifecycleQueryHandler
    : IRequestHandler<GetProjectLifecycleQuery, ProjectLifecycleDto>
{
    public Task<ProjectLifecycleDto> Handle(
        GetProjectLifecycleQuery request,
        CancellationToken cancellationToken)
    {
        var states = Enum.GetValues<ProjectStatus>()
            .Select(status => new ProjectStateDto(
                status.ToString(),
                ProjectStateMachine.GetAllowedTransitions(status)
                    .Select(nextStatus => nextStatus.ToString())
                    .ToArray()))
            .ToArray();

        return Task.FromResult(new ProjectLifecycleDto(states));
    }
}
