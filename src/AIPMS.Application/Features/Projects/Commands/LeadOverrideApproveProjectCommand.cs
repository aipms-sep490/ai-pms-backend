using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record LeadOverrideApproveProjectCommand(
    long ProjectId,
    string ConcurrencyToken,
    string Reason) : IRequest<ProjectDto>;

public sealed class LeadOverrideApproveProjectCommandHandler(
    IProjectRepository repository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    IPublisher publisher)
    : IRequestHandler<LeadOverrideApproveProjectCommand, ProjectDto>
{
    public Task<ProjectDto> Handle(LeadOverrideApproveProjectCommand request, CancellationToken cancellationToken) =>
        repository.InTransactionAsync(token => HandleInTransactionAsync(request, token), cancellationToken);

    private async Task<ProjectDto> HandleInTransactionAsync(
        LeadOverrideApproveProjectCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();

        var actorUserId = currentUser.UserId.Value;
        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal);
        if (!isStaff)
        {
            throw new ForbiddenException(
                isAdmin
                    ? "Platform administrators without DEPARTMENT_STAFF role cannot perform lead override approval."
                    : "Only Lead Department Staff can perform lead override approval.",
                isAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : WorkflowErrorCodes.Forbidden);
        }

        var updatedProject = await repository.LeadOverrideApproveAsync(
            request.ProjectId,
            actorUserId,
            request.ConcurrencyToken,
            request.Reason,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "PROJECT_PROPOSAL_LEAD_OVERRIDE",
                "PROJECT",
                updatedProject.Id,
                new Dictionary<string, object?>
                {
                    ["teamId"] = updatedProject.TeamId,
                    ["title"] = updatedProject.Title,
                    ["reason"] = request.Reason.Trim()
                }),
            cancellationToken);

        await publisher.Publish(
            new WorkflowNotificationEvent(WorkflowNotificationKind.ProjectApproved, updatedProject.Id, actorUserId, updatedProject.UpdatedAt),
            cancellationToken);

        // Mirror pre-approval supervision auto-activation flow
        var confirmation = await repository.GetSupervisorConfirmationAsync(updatedProject.Id, cancellationToken);
        if (confirmation.HasConfirmedPrimarySupervisor && confirmation.MissingMentorMajorIds.Count == 0)
        {
            updatedProject = await repository.UpdateStatusAsync(
                updatedProject.Id, updatedProject.ConcurrencyToken,
                "APPROVED", "SUPERVISOR_PENDING", actorUserId, "Confirm pre-approval supervision (lead override).", cancellationToken);

            if (await repository.IsWorkingAgreementReadyForActivationAsync(updatedProject.Id, cancellationToken))
            {
                updatedProject = await repository.UpdateStatusAsync(
                    updatedProject.Id, updatedProject.ConcurrencyToken,
                    "SUPERVISOR_PENDING", "ACTIVE", actorUserId, "Supervision and working agreement confirmed (lead override).", cancellationToken);
                await auditTrail.RecordAsync(
                    new AuditEntry(
                        actorUserId,
                        "PROJECT_ACTIVATED",
                        "PROJECT",
                        updatedProject.Id,
                        new Dictionary<string, object?>
                        {
                            ["oldStatus"] = "SUPERVISOR_PENDING",
                            ["newStatus"] = "ACTIVE",
                            ["source"] = "LEAD_OVERRIDE_PRE_APPROVAL_SUPERVISION"
                        }),
                    cancellationToken);
            }
        }

        return updatedProject;
    }
}
