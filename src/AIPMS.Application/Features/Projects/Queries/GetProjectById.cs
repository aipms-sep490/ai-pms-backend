using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries;

public sealed record GetProjectByIdQuery(long Id) : IRequest<ProjectDto>;

public sealed class GetProjectByIdQueryHandler(
    IProjectRepository repository,
    IAcademicStructureRepository academicRepository,
    ICurrentUser currentUser)
    : IRequestHandler<GetProjectByIdQuery, ProjectDto>
{
    public async Task<ProjectDto> Handle(
        GetProjectByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve the project
        var project = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Project", request.Id);

        // Security check (anti-IDOR)
        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal);
        long? staffScopeDeptId = null;

        if (isStaff && !isAdmin)
        {
            var scope = await academicRepository.GetUserScopeAsync(actorUserId, cancellationToken);
            if (scope is null || scope.DepartmentId <= 0)
            {
                throw new ForbiddenException("You are not authorized to view this project.");
            }
            staffScopeDeptId = scope.DepartmentId;
        }

        var canView = await repository.CanUserViewProjectAsync(
            request.Id,
            actorUserId,
            isAdmin,
            staffScopeDeptId,
            cancellationToken);

        if (!canView)
        {
            throw new ForbiddenException("You are not authorized to view this project.");
        }

        return project;
    }
}
