using AIPMS.Application.Features.WorkflowContext.DTOs;

namespace AIPMS.Application.Features.WorkflowContext.Abstractions;

public interface IWorkflowContextReader
{
    Task<UserWorkflowContextDto> GetCurrentAsync(long userId, IReadOnlyCollection<string> tokenRoles, long? semesterId, CancellationToken ct);
    Task<TeamWorkflowActionsDto> GetTeamActionsAsync(long userId, IReadOnlyCollection<string> tokenRoles, long teamId, CancellationToken ct);
    Task<ProjectWorkflowActionsDto> GetProjectActionsAsync(long userId, IReadOnlyCollection<string> tokenRoles, long projectId, CancellationToken ct);
}
