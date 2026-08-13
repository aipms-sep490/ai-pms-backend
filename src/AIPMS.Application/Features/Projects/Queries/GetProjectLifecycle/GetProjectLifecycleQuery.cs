using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Domain.Entities;
using AIPMS.Domain.Enums;

namespace AIPMS.Application.Features.Projects.Queries.GetProjectLifecycle;

public sealed class GetProjectLifecycleQuery
{
    public ProjectLifecycleDto Execute()
    {
        var states = Enum.GetValues<ProjectStatus>()
            .Select(status => new ProjectStateDto(
                status.ToString(),
                ProjectStateMachine.GetAllowedTransitions(status)
                    .Select(nextStatus => nextStatus.ToString())
                    .ToArray()))
            .ToArray();

        return new ProjectLifecycleDto(states);
    }
}
