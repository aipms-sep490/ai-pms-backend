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
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Domain.Projects;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record ArchiveProjectCommand(long ProjectId, string ConcurrencyToken, string? Reason) : IRequest<ProjectDto>;

public sealed class ArchiveProjectCommandHandler(
    IProjectRepository repository,
    IAcademicStructureRepository academicRepository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail) : IRequestHandler<ArchiveProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(ArchiveProjectCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) throw new UnauthorizedException();
        var actorId = currentUser.UserId.Value;
        var project = await repository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);
        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal);
        if (!isAdmin && !isStaff) throw new ForbiddenException("Only academic staff can archive projects.");
        if (isStaff && !isAdmin)
        {
            var scope = await academicRepository.GetUserScopeAsync(actorId, cancellationToken);
            var departmentIds = await repository.GetProjectMajorDepartmentIdsAsync(request.ProjectId, cancellationToken);
            if (scope is null || !departmentIds.Contains(scope.DepartmentId))
                throw new ForbiddenException("You can only archive projects within your academic department scope.");
        }
        var current = Enum.Parse<ProjectStatus>(project.Status.Replace("_", ""), true);
        if (current != ProjectStatus.Completed)
            throw new ConflictException("Only a COMPLETED project can be archived.");
        var updated = await repository.UpdateStatusAsync(request.ProjectId, request.ConcurrencyToken,
            project.Status, "ARCHIVED", actorId, request.Reason?.Trim(), cancellationToken);
        await auditTrail.RecordAsync(new AuditEntry(actorId, "PROJECT_ARCHIVED", "PROJECT", updated.Id,
            new Dictionary<string, object?> { ["previousStatus"] = project.Status, ["reason"] = request.Reason?.Trim() }), cancellationToken);
        return updated;
    }
}
