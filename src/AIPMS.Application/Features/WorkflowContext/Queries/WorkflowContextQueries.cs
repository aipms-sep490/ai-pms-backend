using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.WorkflowContext.Abstractions;
using AIPMS.Application.Features.WorkflowContext.DTOs;
using MediatR;

namespace AIPMS.Application.Features.WorkflowContext.Queries;

public sealed record GetUserWorkflowContextQuery(long? AcademicSemesterId = null) : IRequest<UserWorkflowContextDto>;
public sealed record GetTeamWorkflowActionsQuery(long TeamId) : IRequest<TeamWorkflowActionsDto>;
public sealed record GetProjectWorkflowActionsQuery(long ProjectId) : IRequest<ProjectWorkflowActionsDto>;

public sealed class WorkflowContextQueryHandler(IWorkflowContextReader reader, ICurrentUser currentUser)
    : IRequestHandler<GetUserWorkflowContextQuery, UserWorkflowContextDto>,
      IRequestHandler<GetTeamWorkflowActionsQuery, TeamWorkflowActionsDto>,
      IRequestHandler<GetProjectWorkflowActionsQuery, ProjectWorkflowActionsDto>
{
    private long ActorId => currentUser.IsAuthenticated && currentUser.UserId is long id ? id : throw new UnauthorizedException();
    public Task<UserWorkflowContextDto> Handle(GetUserWorkflowContextQuery request, CancellationToken ct) =>
        reader.GetCurrentAsync(ActorId, currentUser.Roles, request.AcademicSemesterId, ct);
    public Task<TeamWorkflowActionsDto> Handle(GetTeamWorkflowActionsQuery request, CancellationToken ct) =>
        reader.GetTeamActionsAsync(ActorId, currentUser.Roles, request.TeamId, ct);
    public Task<ProjectWorkflowActionsDto> Handle(GetProjectWorkflowActionsQuery request, CancellationToken ct) =>
        reader.GetProjectActionsAsync(ActorId, currentUser.Roles, request.ProjectId, ct);
}
