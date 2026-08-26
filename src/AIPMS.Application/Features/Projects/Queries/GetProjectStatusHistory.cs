using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries;

public sealed record GetProjectStatusHistoryQuery(long ProjectId) : IRequest<IReadOnlyList<ProjectStatusHistoryDto>>;

public sealed class GetProjectStatusHistoryQueryHandler(
    IProjectRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<GetProjectStatusHistoryQuery, IReadOnlyList<ProjectStatusHistoryDto>>
{
    public async Task<IReadOnlyList<ProjectStatusHistoryDto>> Handle(
        GetProjectStatusHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve project check (if project doesn't exist, throw NotFound)
        var project = await repository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        // Security check (anti-IDOR)
        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal);

        var canView = await repository.CanUserViewProjectAsync(
            request.ProjectId,
            actorUserId,
            isAdmin || isStaff,
            cancellationToken);

        if (!canView)
        {
            throw new ForbiddenException("You are not authorized to view this project's status history.");
        }

        var history = await repository.GetStatusHistoryAsync(request.ProjectId, cancellationToken);
        return history;
    }
}
