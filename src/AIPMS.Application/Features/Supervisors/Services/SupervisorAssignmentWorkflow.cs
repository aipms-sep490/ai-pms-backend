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
        return assignment.ToDto();
    }

    public async Task<PagedResult<SupervisorAssignmentDto>> ListAsync(long? projectId, string? status,
        int page, int pageSize, CancellationToken ct)
    {
        var actor = await access.EnsureCanReadAsync(ct);
        if (projectId.HasValue)
        {
            await RequireProjectReaderAsync(actor, projectId.Value, ct);
            if (!await repository.ProjectExistsAsync(projectId.Value, ct))
                throw new NotFoundException("Project", projectId.Value);
        }
        else if (!actor.HasActiveAcademicScope || !actor.Roles.Contains(AppRoles.Lecturer))
            throw new ForbiddenException("Only lecturers can view their own assignments.");
        var result = await repository.SearchAsync(new(projectId, projectId.HasValue ? null : actor.UserId,
            status, page, pageSize), ct);
        return new(result.Items.Select(a => a.ToDto()).ToArray(), result.Page, result.PageSize, result.TotalCount);
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
                    && await repository.IsProjectDepartmentAsync(before.ProjectId, departmentId, token));
            if (!permitted) throw new ForbiddenException("You cannot end this supervisor assignment.");
            if (before.EndedAt.HasValue) return before.ToDto();
            if (before.ProjectStatus is not ("COMPLETED" or "ARCHIVED"))
                throw new ConflictException("Only assignments on completed or archived projects can be ended. Supervisor replacement is not supported.");
            var now = clock.GetUtcNow().UtcDateTime;
            if (now < before.AssignedAt)
                throw new ConflictException("The assignment cannot end before its assigned time.");
            var after = await repository.EndAsync(assignmentId, now, token);
            await audit.RecordAsync(new AuditEntry(actor.UserId, "SUPERVISOR_ASSIGNMENT_ENDED", "SUPERVISOR_ASSIGNMENT",
                assignmentId, new Dictionary<string, object?> { ["before"] = before.ToDto(),
                    ["after"] = after.ToDto(), ["reason"] = reason.Trim() }), token);
            return after.ToDto();
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
}
