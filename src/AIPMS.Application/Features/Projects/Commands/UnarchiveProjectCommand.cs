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
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Services;
using AIPMS.Domain.Projects;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record UnarchiveProjectCommand(long ProjectId, string ConcurrencyToken, string Reason) : IRequest<ProjectDto>;

public sealed class UnarchiveProjectCommandHandler(
    IProjectRepository repository,
    IAcademicStructureRepository academicRepository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    IUserAccountRepository users) : IRequestHandler<UnarchiveProjectCommand, ProjectDto>
{
    public Task<ProjectDto> Handle(UnarchiveProjectCommand request, CancellationToken ct) => repository.InTransactionAsync(async cancellationToken =>
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) throw new UnauthorizedException();
        var actorId = currentUser.UserId.Value;
        var project = await repository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);
        var actor = await users.GetUserAsync(actorId, cancellationToken);
        if (actor is null || actor.Status != "ACTIVE") throw new UnauthorizedException();

        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal) && actor.Roles.Contains(AppRoles.Admin);
        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal) && actor.Roles.Contains(AppRoles.DepartmentStaff);
        if (!isStaff)
            throw new ForbiddenException(
                isAdmin
                    ? "Platform administrators without DEPARTMENT_STAFF role cannot unarchive academic projects."
                    : "Only lead department staff can unarchive projects.",
                isAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : WorkflowErrorCodes.Forbidden);

        long? departmentId = null;
        if (actor.DepartmentId is long id)
        {
            var department = await academicRepository.GetDepartmentAsync(id, cancellationToken);
            if (department is { IsActive: true }
                && await academicRepository.GetOrganizationAsync(department.OrganizationId, cancellationToken) is { IsActive: true })
                departmentId = id;
        }

        var departmentIds = await repository.GetProjectMajorDepartmentIdsAsync(request.ProjectId, cancellationToken);
        var leadDepartmentId = await repository.GetLeadDepartmentIdAsync(request.ProjectId, cancellationToken);
        if (!ProjectArchivePolicy.HasLeadScope(isStaff, departmentId, leadDepartmentId, departmentIds)
            || !await repository.CanUserViewProjectAsync(request.ProjectId, actorId, false, departmentId, cancellationToken))
            throw new ForbiddenException("Only the frozen lead department can unarchive this project.", WorkflowErrorCodes.ScopeAccessDenied);

        if (!Enum.TryParse<ProjectStatus>(project.Status.Replace("_", ""), true, out var currentStatus)
            || !ProjectStateMachine.CanUnarchive(currentStatus))
            throw new ConflictException("Only an ARCHIVED project can be unarchived back to COMPLETED.", WorkflowErrorCodes.ArchiveNotAllowed);

        var reason = request.Reason.Trim();
        var updated = await repository.UpdateStatusAsync(
            request.ProjectId,
            request.ConcurrencyToken,
            "ARCHIVED",
            "COMPLETED",
            actorId,
            reason,
            cancellationToken);

        await auditTrail.RecordAsync(new AuditEntry(
            actorId,
            "PROJECT_UNARCHIVED",
            "PROJECT",
            updated.Id,
            new Dictionary<string, object?>
            {
                ["previousStatus"] = "ARCHIVED",
                ["newStatus"] = "COMPLETED",
                ["reason"] = reason
            }), cancellationToken);

        return updated;
    }, ct);
}
