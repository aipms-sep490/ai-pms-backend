using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Models;

namespace AIPMS.Application.Features.Supervisors.Services;

public sealed class SupervisorAssignmentWorkflow(ISupervisorAssignmentRepository repository,
    SupervisorAccessService access, IProjectAccessService projectAccess, IAuditTrail audit, TimeProvider clock)
{
    public async Task<SupervisorAssignmentDto> GetAsync(long assignmentId, CancellationToken ct)
    {
        var actor = await access.EnsureCanReadAsync(ct);
        var assignment = await RequireAssignmentAsync(assignmentId, ct);
        // Former supervisors retain access to their own assignment record, not the project workspace.
        if (!IsOwner(actor, assignment)) await RequireProjectReaderAsync(actor, assignment.ProjectId, ct);
        return await WithCapabilitiesAsync(actor, assignment, ct);
    }

    public async Task<PagedResult<SupervisorAssignmentDto>> ListAsync(long? projectId, string? status,
        int page, int pageSize, CancellationToken ct)
    {
        var actor = await access.EnsureCanReadAsync(ct);
        if (projectId.HasValue)
        {
            if (actor.Roles.Contains(AppRoles.Admin) && !await repository.ProjectExistsAsync(projectId.Value, ct))
                throw new NotFoundException("Project", projectId.Value);
            await RequireProjectReaderAsync(actor, projectId.Value, ct);
            if (!await repository.ProjectExistsAsync(projectId.Value, ct))
                throw new NotFoundException("Project", projectId.Value);
        }
        else if (!actor.HasActiveAcademicScope || !actor.Roles.Contains(AppRoles.Lecturer))
            throw new ForbiddenException("Only lecturers can view their own assignments.");
        var result = await repository.SearchAsync(new(projectId, projectId.HasValue ? null : actor.UserId,
            status, page, pageSize), ct);
        IReadOnlySet<long> replaceable = new HashSet<long>();
        if (projectId is long scopeProject && actor.HasActiveAcademicScope && actor.Roles.Contains(AppRoles.DepartmentStaff)
            && actor.DepartmentId is long departmentId)
        {
            replaceable = await repository.GetReplaceableIdsAsync(scopeProject, departmentId, ct);
        }
        var items = new List<SupervisorAssignmentDto>(result.Items.Count);
        foreach (var item in result.Items) items.Add(await WithCapabilitiesAsync(actor, item, ct,
            replaceable.Contains(item.Id)));
        return new(items, result.Page, result.PageSize, result.TotalCount);
    }

    public Task<SupervisorAssignmentDto> EndAsync(long assignmentId, string reason, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await access.EnsureCanReadAsync(token);
            await repository.LockAsync(assignmentId, token);
            var before = await RequireAssignmentAsync(assignmentId, token);
            var permitted = actor.Roles.Contains(AppRoles.Admin) || IsOwner(actor, before)
                || (actor.HasActiveAcademicScope && actor.Roles.Contains(AppRoles.DepartmentStaff)
                    && actor.DepartmentId is long departmentId
                    && await repository.IsAssignmentDepartmentAsync(before.Id, departmentId, token));
            if (!permitted) throw new ForbiddenException("You cannot end this supervisor assignment.");
            if (before.EndedAt.HasValue) return await WithCapabilitiesAsync(actor, before, token);
            if (!before.HasKnownAcademicScope) throw new ConflictException("ACADEMIC_SCOPE_UNKNOWN");
            if (before.ProjectStatus != "ACTIVE")
                throw new ConflictException("Only assignments on ACTIVE projects can be ended. Completed and archived projects are read-only.");
            var now = clock.GetUtcNow().UtcDateTime;
            if (now < before.AssignedAt)
                throw new ConflictException("The assignment cannot end before its assigned time.");
            var after = await repository.EndAsync(assignmentId, now, token, actor.UserId, reason.Trim());
            await audit.RecordAsync(new AuditEntry(actor.UserId, "SUPERVISOR_ASSIGNMENT_ENDED", "SUPERVISOR_ASSIGNMENT",
                assignmentId, new Dictionary<string, object?> { ["before"] = before.ToDto(),
                    ["after"] = after.ToDto(), ["reason"] = reason.Trim() }), token);
            return await WithCapabilitiesAsync(actor, after, token);
        }, ct);

    private static bool IsOwner(SupervisorAccount actor, SupervisorAssignmentModel assignment) =>
        actor.HasActiveAcademicScope && actor.Roles.Contains(AppRoles.Lecturer) && actor.UserId == assignment.SupervisorUserId;

    private async Task RequireProjectReaderAsync(SupervisorAccount actor, long projectId, CancellationToken ct)
    {
        if ((!actor.Roles.Contains(AppRoles.Admin) && !actor.HasActiveAcademicScope)
            || !await projectAccess.CanAccessAsync(actor.UserId, projectId, ct))
            throw new ForbiddenException("You cannot view assignments for this project.");
    }

    private async Task<SupervisorAssignmentModel> RequireAssignmentAsync(long id, CancellationToken ct) =>
        await repository.GetAsync(id, ct) ?? throw new NotFoundException("SupervisorAssignment", id);

    private async Task<SupervisorAssignmentDto> WithCapabilitiesAsync(SupervisorAccount actor,
        SupervisorAssignmentModel assignment, CancellationToken ct, bool? replaceable = null)
    {
        var reasons = new List<string>();
        var canReplace = false;
        var canEnd = false;
        var isStaff = actor.HasActiveAcademicScope && actor.Roles.Contains(AppRoles.DepartmentStaff);
        if (!assignment.HasKnownAcademicScope) reasons.Add("ACADEMIC_SCOPE_UNKNOWN");
        if (assignment.EndedAt.HasValue) reasons.Add("ASSIGNMENT_ENDED");
        if (assignment.ProjectStatus is "COMPLETED" or "ARCHIVED") reasons.Add("PROJECT_READ_ONLY");
        if (isStaff && assignment.HasKnownAcademicScope && assignment.ProjectStatus == "ACTIVE" && !assignment.EndedAt.HasValue)
        {
            // Replacement authority is ultimately revalidated by the mutation service; this read model only exposes a safe preview.
            canReplace = replaceable ?? (actor.DepartmentId is long departmentId
                && await repository.CanReplaceAsync(assignment.Id, departmentId, ct));
            if (!canReplace) reasons.Add("OUTSIDE_ASSIGNMENT_SCOPE");
        }
        else if (assignment.ProjectStatus != "ACTIVE") reasons.Add("REPLACEMENT_REQUIRES_ACTIVE_PROJECT");
        var owner = IsOwner(actor, assignment);
        canEnd = assignment.HasKnownAcademicScope && !assignment.EndedAt.HasValue && assignment.ProjectStatus == "ACTIVE"
            && (actor.Roles.Contains(AppRoles.Admin) || owner || canReplace);
        if (!canEnd && assignment.ProjectStatus != "ACTIVE") reasons.Add("END_REQUIRES_ACTIVE_PROJECT");
        return assignment.ToDto() with
        {
            AllowedActions = new[] { new SupervisorActionCapabilityDto("REPLACE", canReplace), new SupervisorActionCapabilityDto("END", canEnd) },
            Reasons = reasons.Distinct().ToArray()
        };
    }
}
