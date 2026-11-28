using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Queries;

public sealed record GetOverdueBlockedTasksQuery(long ProjectId) : IRequest<OverdueBlockedTasksDto>;

public sealed class GetOverdueBlockedTasksQueryHandler(
    ITaskRepository repository,
    IProjectRepository projectRepository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser,
    IProjectScopeResolver? scopeResolver = null)
    : IRequestHandler<GetOverdueBlockedTasksQuery, OverdueBlockedTasksDto>
{
    public async Task<OverdueBlockedTasksDto> Handle(
        GetOverdueBlockedTasksQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Verify project existence
        var project = await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        // Verify project access
        if (!await projectAccessService.CanAccessAsync(actorUserId, request.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        IReadOnlyCollection<long>? allowedMajorIds = null;
        if (scopeResolver is not null)
        {
            var actorScope = await scopeResolver.ResolveAsync(actorUserId, request.ProjectId, cancellationToken);
            if (actorScope is { IsFullProjectAccess: false, IsMentor: true })
            {
                allowedMajorIds = actorScope.AllowedMajorIds;
            }
        }

        var (overdue, blocked) = allowedMajorIds is not null
            ? await repository.GetOverdueAndBlockedScopedAsync(request.ProjectId, allowedMajorIds, cancellationToken)
            : await repository.GetOverdueAndBlockedAsync(request.ProjectId, cancellationToken);
        return new OverdueBlockedTasksDto(overdue, blocked);
    }
}
