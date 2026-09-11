using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Domain.Supervisors;
using AIPMS.Application.Features.Notifications.Events;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Services;

public sealed class SupervisorRequestWorkflow(ISupervisorRequestRepository repository,
    ISupervisorCandidateRepository candidates, ISupervisorProfileRepository profiles,
    SupervisorAccessService access, IProjectAccessService projectAccess, IAuditTrail audit, TimeProvider clock,
    IPublisher events)
{
    public Task<SupervisorRequestDto> SendAsync(long projectId, long profileId, string? message, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await access.EnsureCanReadAsync(token);
            await RequireLeaderAsync(actor, projectId, token);
            await repository.LockSupervisorAndProjectAsync(profileId, projectId, token);
            var now = clock.GetUtcNow().UtcDateTime;
            await RequireEligibilityAsync(projectId, profileId, now, false, token);
            if (await repository.HasPendingAsync(projectId, profileId, token))
                throw new ConflictException("A pending request already exists for this project and supervisor.");
            var request = await repository.CreateAsync(projectId, profileId, actor.UserId, message?.Trim(), now, token);
            await events.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.SupervisorRequestSent,
                request.Id, actor.UserId, now), token);
            await AuditAsync("SUPERVISOR_REQUEST_SENT", actor.UserId, null, request, token);
            return request.ToDto();
        }, ct);

    public Task<SupervisorRequestDto> CancelAsync(long requestId, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await access.EnsureCanReadAsync(token);
            await repository.LockRequestAsync(requestId, token);
            var request = await GetAsync(requestId, token);
            await RequireLeaderAsync(actor, request.ProjectId, token);
            if (request.Status == "CANCELLED") return request.ToDto();
            RequirePending(request);
            var now = clock.GetUtcNow().UtcDateTime;
            var result = await repository.RespondAsync(request.Id, "CANCELLED", null, now, token);
            await events.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.SupervisorRequestCancelled,
                request.Id, actor.UserId, now), token);
            await AuditAsync("SUPERVISOR_REQUEST_CANCELLED", actor.UserId, request, result, token);
            return result.ToDto();
        }, ct);

    public Task<SupervisorRequestDto> RespondAsync(long requestId, bool accept, string? message, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await access.EnsureCanReadAsync(token);
            await repository.LockRequestAsync(requestId, token);
            var request = await GetAsync(requestId, token);
            if (actor.UserId != request.SupervisorUserId || !actor.HasActiveAcademicScope
                || !actor.Roles.Contains(AppRoles.Lecturer))
                throw new ForbiddenException("Only the requested lecturer can respond to this request.");

            var status = accept ? "ACCEPTED" : "REJECTED";
            // Replays return the existing decision, including after completion/end of assignment.
            if (request.Status == status)
            {
                if (accept && request.AssignmentId is null)
                    throw new ConflictException("The accepted request has no matching assignment.");
                return request.ToDto();
            }
            RequirePending(request);
            var now = clock.GetUtcNow().UtcDateTime;
            string? previousProjectStatus = null;
            if (accept)
            {
                await repository.LockSupervisorAndProjectAsync(request.SupervisorProfileId, request.ProjectId, token);
                now = clock.GetUtcNow().UtcDateTime;
                var project = await RequireEligibilityAsync(request.ProjectId, request.SupervisorProfileId, now, true, token);
                previousProjectStatus = project.Status;
                await repository.AssignAndActivateAsync(request, actor.UserId, now, token);
                foreach (var other in await repository.GetOtherPendingAsync(request.ProjectId, request.Id, token))
                {
                    var cancelled = await repository.RespondAsync(other.Id, "CANCELLED", null, now, token);
                    await events.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.SupervisorRequestCancelled,
                        other.Id, actor.UserId, now), token);
                    await AuditAsync("SUPERVISOR_REQUEST_CANCELLED", actor.UserId, other, cancelled, token, request.Id);
                }
            }
            var result = await repository.RespondAsync(request.Id, status, message?.Trim(), now, token);
            await events.Publish(new WorkflowNotificationEvent(accept ? WorkflowNotificationKind.SupervisorRequestAccepted
                : WorkflowNotificationKind.SupervisorRequestRejected, request.Id, actor.UserId, now), token);
            await AuditAsync(accept ? "SUPERVISOR_REQUEST_ACCEPTED" : "SUPERVISOR_REQUEST_REJECTED",
                actor.UserId, request, result, token);
            if (accept)
            {
                await audit.RecordAsync(new AuditEntry(actor.UserId, "SUPERVISOR_ASSIGNED", "SUPERVISOR_ASSIGNMENT",
                    result.AssignmentId, new Dictionary<string, object?> { ["requestId"] = request.Id,
                        ["projectId"] = request.ProjectId, ["supervisorProfileId"] = request.SupervisorProfileId }), token);
                await audit.RecordAsync(new AuditEntry(actor.UserId, "PROJECT_ACTIVATED", "PROJECT",
                    request.ProjectId, new Dictionary<string, object?> { ["requestId"] = request.Id,
                        ["assignmentId"] = result.AssignmentId, ["oldStatus"] = previousProjectStatus, ["newStatus"] = "ACTIVE" }), token);
            }
            return result.ToDto();
        }, ct);

    public async Task<PagedResult<SupervisorRequestDto>> ListAsync(long? projectId, string? status,
        int page, int pageSize, CancellationToken ct)
    {
        var actor = await access.EnsureCanReadAsync(ct);
        if (projectId.HasValue)
        {
            if ((!actor.Roles.Contains(AppRoles.Admin) && !actor.HasActiveAcademicScope)
                || !await projectAccess.CanAccessAsync(actor.UserId, projectId.Value, ct))
                throw new ForbiddenException("You cannot view requests for this project.");
        }
        else if (!actor.Roles.Contains(AppRoles.Lecturer) || !actor.HasActiveAcademicScope)
            throw new ForbiddenException("Only lecturers can view their supervisor request inbox.");
        var result = await repository.SearchAsync(new(projectId, projectId.HasValue ? null : actor.UserId,
            status, page, pageSize), ct);
        return new(result.Items.Select(r => r.ToDto()).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    private async Task RequireLeaderAsync(SupervisorAccount actor, long projectId, CancellationToken ct)
    {
        if (!actor.HasActiveAcademicScope || !actor.Roles.Contains(AppRoles.Student)
            || !await repository.IsTeamLeaderAsync(projectId, actor.UserId, ct))
            throw new ForbiddenException("Only the current student team leader can send or cancel supervisor requests.");
    }

    private async Task<SupervisorCandidateProject> RequireEligibilityAsync(long projectId, long profileId, DateTime now, bool accepting, CancellationToken ct)
    {
        var project = await candidates.GetProjectAsync(projectId, now, ct) ?? throw new NotFoundException("Project", projectId);
        if ((project.Status != "APPROVED" && !(accepting && project.Status == "SUPERVISOR_PENDING"))
            || project.HasActiveAssignment || !project.HasActiveSemester || project.DepartmentIds.Count == 0)
            throw new ConflictException("The project is not eligible for supervisor selection.");
        var policies = await candidates.GetSelectionPoliciesAsync(project.AcademicSemesterId, now, ct);
        if (policies.Count != 1 || policies[0].MaxProjectsPerSupervisor is not > 0)
            throw new ConflictException("One active supervisor-selection period with a configured quota is required.");
        var profile = await profiles.GetAsync(profileId, ct) ?? throw new NotFoundException("SupervisorProfile", profileId);
        if (!profile.IsAvailable || !project.DepartmentIds.Contains(profile.DepartmentId))
            throw new ConflictException("The supervisor is unavailable or outside the project's academic scope.");
        var workload = await repository.GetWorkloadAsync(profileId, project.AcademicSemesterId, ct);
        if (new SupervisorCapacity(workload.ProfileLimit, policies[0].MaxProjectsPerSupervisor!.Value,
                workload.ActiveProjects, workload.SemesterActiveProjects).RemainingSlots == 0)
            throw new ConflictException("The supervisor has reached the profile or semester capacity limit.");
        return project;
    }

    private async Task<SupervisorRequestModel> GetAsync(long id, CancellationToken ct) =>
        await repository.GetAsync(id, ct) ?? throw new NotFoundException("SupervisorRequest", id);

    private static void RequirePending(SupervisorRequestModel request)
    {
        if (request.Status != "PENDING") throw new ConflictException("The request has already been processed.");
    }

    private Task AuditAsync(string action, long actorId, SupervisorRequestModel? before,
        SupervisorRequestModel after, CancellationToken ct, long? acceptedRequestId = null) =>
        audit.RecordAsync(new AuditEntry(actorId, action, "SUPERVISOR_REQUEST", after.Id,
            new Dictionary<string, object?> { ["before"] = before?.ToDto(), ["after"] = after.ToDto(),
                ["acceptedRequestId"] = acceptedRequestId }), ct);
}
