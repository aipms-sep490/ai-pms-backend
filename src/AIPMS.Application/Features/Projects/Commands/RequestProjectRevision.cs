using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Domain.Entities;
using AIPMS.Domain.Enums;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record RequestProjectRevisionCommand(
    long ProjectId,
    string ConcurrencyToken,
    string Reason) : IRequest<ProjectDto>;

public sealed class RequestProjectRevisionCommandValidator : AbstractValidator<RequestProjectRevisionCommand>
{
    public RequestProjectRevisionCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Revision reason is mandatory.")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Revision reason cannot be whitespace.");
    }
}

public sealed class RequestProjectRevisionCommandHandler(
    IProjectRepository repository,
    IAcademicStructureRepository academicRepository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<RequestProjectRevisionCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(
        RequestProjectRevisionCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Reason is mandatory
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ConflictException("Revision reason is mandatory and cannot be empty.");
        }

        // Retrieve the project
        var project = await repository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        // Authorization checks (Admin or Department Staff in scope)
        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal);

        if (!isAdmin && !isStaff)
        {
            throw new ForbiddenException("Only Admin or Department Staff can request revisions on project proposals.");
        }

        if (isStaff && !isAdmin)
        {
            var staffScope = await academicRepository.GetUserScopeAsync(actorUserId, cancellationToken);
            if (staffScope is null)
            {
                throw new ForbiddenException("Your account does not have a configured academic department scope.");
            }

            var projectDeptIds = await repository.GetProjectMajorDepartmentIdsAsync(request.ProjectId, cancellationToken);
            if (!projectDeptIds.Contains(staffScope.DepartmentId))
            {
                throw new ForbiddenException("You can only review projects within your assigned academic department scope.");
            }
        }

        // Validate state machine transition
        var currentStatus = Enum.Parse<ProjectStatus>(project.Status.Replace("_", ""), ignoreCase: true);
        if (!ProjectStateMachine.CanTransition(currentStatus, ProjectStatus.RevisionRequired))
        {
            throw new ConflictException($"Cannot transition project from status {project.Status} to REVISION_REQUIRED.");
        }

        // Update project status to REVISION_REQUIRED and write history (all within repository transaction)
        var updatedProject = await repository.UpdateStatusAsync(
            request.ProjectId,
            request.ConcurrencyToken,
            project.Status,
            "REVISION_REQUIRED",
            actorUserId,
            request.Reason.Trim(),
            cancellationToken);

        // Audit the action
        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "PROJECT_REVISION_REQUESTED",
                "PROJECT",
                updatedProject.Id,
                new Dictionary<string, object?>
                {
                    ["teamId"] = updatedProject.TeamId,
                    ["title"] = updatedProject.Title,
                    ["reason"] = request.Reason
                }),
            cancellationToken);

        return updatedProject;
    }
}
