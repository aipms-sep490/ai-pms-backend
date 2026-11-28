using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Services;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries;

public sealed record GetProjectArchiveDossierQuery(long ProjectId) : IRequest<ProjectArchiveDossierDto>;

public sealed class GetProjectArchiveDossierQueryHandler(
    IProjectRepository repository,
    IAcademicStructureRepository academicRepository,
    ICurrentUser currentUser,
    IUserAccountRepository users) : IRequestHandler<GetProjectArchiveDossierQuery, ProjectArchiveDossierDto>
{
    public async Task<ProjectArchiveDossierDto> Handle(GetProjectArchiveDossierQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) throw new UnauthorizedException();
        var actorId = currentUser.UserId.Value;
        var project = await repository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        var actor = await users.GetUserAsync(actorId, cancellationToken);
        if (actor is null || actor.Status != "ACTIVE") throw new UnauthorizedException();

        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal) && actor.Roles.Contains(AppRoles.Admin);
        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal) && actor.Roles.Contains(AppRoles.DepartmentStaff);
        long? departmentId = null;
        if (isStaff && actor.DepartmentId is long id)
        {
            var department = await academicRepository.GetDepartmentAsync(id, cancellationToken);
            if (department is { IsActive: true }
                && await academicRepository.GetOrganizationAsync(department.OrganizationId, cancellationToken) is { IsActive: true })
                departmentId = id;
        }

        if (!await repository.CanUserViewProjectAsync(request.ProjectId, actorId, isAdmin, departmentId, cancellationToken))
            throw new ForbiddenException("You do not have permission to view this project's archive dossier.", WorkflowErrorCodes.ScopeAccessDenied);

        if (!string.Equals(project.Status, "ARCHIVED", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Archive dossier is only available for ARCHIVED projects.", WorkflowErrorCodes.ArchiveNotAllowed);

        var departmentIds = await repository.GetProjectMajorDepartmentIdsAsync(request.ProjectId, cancellationToken);
        var leadDepartmentId = await repository.GetLeadDepartmentIdAsync(request.ProjectId, cancellationToken);
        var isLeadStaff = ProjectArchivePolicy.HasLeadScope(isStaff, departmentId, leadDepartmentId, departmentIds);

        return await repository.GetArchiveDossierAsync(request.ProjectId, actorId, isAdmin, departmentId, isLeadStaff, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);
    }
}
