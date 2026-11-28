using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Queries;

public sealed record GetTasksQuery(
    long ProjectId,
    long? MilestoneId = null,
    string? Status = null,
    string? Priority = null,
    long? AssigneeUserId = null,
    string? Search = null,
    DateTime? DueFrom = null,
    DateTime? DueTo = null,
    bool? IsOverdue = null,
    bool? IsBlocked = null,
    int Page = 1,
    int PageSize = 10,
    long? MajorId = null,
    string? DisciplineRole = null) : IRequest<PagedResult<TaskDto>>;

public sealed class GetTasksQueryHandler(
    ITaskRepository repository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser,
    IProjectScopeResolver? scopeResolver = null)
    : IRequestHandler<GetTasksQuery, PagedResult<TaskDto>>
{
    public async Task<PagedResult<TaskDto>> Handle(
        GetTasksQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Verify project access
        if (!await projectAccessService.CanAccessAsync(actorUserId, request.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        // Verify major belongs to project if specified
        if (request.MajorId.HasValue)
        {
            if (!await repository.MajorBelongsToProjectAsync(request.MajorId.Value, request.ProjectId, cancellationToken))
            {
                throw new ForbiddenException("The requested major does not belong to this project.");
            }
        }

        // TK-W2-05: Enforce Discipline Mentor major scope isolation on task lists.
        IReadOnlyCollection<long>? allowedMajorIds = null;
        if (scopeResolver is not null)
        {
            var actorScope = await scopeResolver.ResolveAsync(actorUserId, request.ProjectId, cancellationToken);
            if (actorScope is { IsFullProjectAccess: false, IsMentor: true })
            {
                if (request.MajorId.HasValue && !actorScope.CanAccessMajor(request.MajorId.Value))
                {
                    throw new ForbiddenException("The requested major is outside your assigned mentor scope.", WorkflowErrorCodes.ScopeAccessDenied);
                }
                allowedMajorIds = actorScope.AllowedMajorIds;
            }
        }

        if (allowedMajorIds is not null)
        {
            return await repository.GetTasksScopedAsync(
                request.ProjectId,
                request.MilestoneId,
                request.Status,
                request.Priority,
                request.AssigneeUserId,
                request.Search,
                request.DueFrom,
                request.DueTo,
                request.IsOverdue,
                request.IsBlocked,
                request.Page,
                request.PageSize,
                request.MajorId,
                allowedMajorIds,
                cancellationToken,
                request.DisciplineRole);
        }

        return await repository.GetTasksAsync(
            request.ProjectId,
            request.MilestoneId,
            request.Status,
            request.Priority,
            request.AssigneeUserId,
            request.Search,
            request.DueFrom,
            request.DueTo,
            request.IsOverdue,
            request.IsBlocked,
            request.Page,
            request.PageSize,
            request.MajorId,
            cancellationToken, request.DisciplineRole);
    }
}
