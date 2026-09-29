using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.Projects.Services;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record ArchiveProjectCommand(long ProjectId, string ConcurrencyToken, string? Reason) : IRequest<ProjectDto>;

public sealed class ArchiveProjectCommandHandler(
    IProjectRepository repository,
    IAcademicStructureRepository academicRepository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    IUserAccountRepository users) : IRequestHandler<ArchiveProjectCommand, ProjectDto>
{
    public Task<ProjectDto> Handle(ArchiveProjectCommand request, CancellationToken ct) => repository.InTransactionAsync(async cancellationToken =>
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
        var departmentIds = await repository.GetProjectMajorDepartmentIdsAsync(request.ProjectId, cancellationToken);
        if (!ProjectArchivePolicy.HasScope(isAdmin, isStaff, departmentId, departmentIds)
            || !await repository.CanUserViewProjectAsync(request.ProjectId, actorId, isAdmin, departmentId, cancellationToken))
            throw new ForbiddenException("You can only archive projects within your academic department scope.");
        if (!ProjectArchivePolicy.HasState(project.Status))
            throw new ConflictException("Only a COMPLETED project can be archived.", WorkflowErrorCodes.ArchiveNotAllowed);
        var updated = await repository.UpdateStatusAsync(request.ProjectId, request.ConcurrencyToken,
            project.Status, "ARCHIVED", actorId, request.Reason?.Trim(), cancellationToken);
        await auditTrail.RecordAsync(new AuditEntry(actorId, "PROJECT_ARCHIVED", "PROJECT", updated.Id,
            new Dictionary<string, object?> { ["previousStatus"] = project.Status, ["reason"] = request.Reason?.Trim() }), cancellationToken);
        return updated;
    }, ct);
}
