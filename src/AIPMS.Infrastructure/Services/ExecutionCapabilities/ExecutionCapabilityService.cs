using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ExecutionCapabilities.Abstractions;
using AIPMS.Application.Features.ExecutionCapabilities.DTOs;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.WorkflowContext.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Services.ExecutionCapabilities;

internal sealed class ExecutionCapabilityService(
    AipmsDbContext db,
    ICurrentUser current,
    IProjectAccessService access,
    IWorkflowContextReader workflow,
    ITaskRepository tasks) : IExecutionCapabilityService
{
    private long Actor => current.UserId ?? throw new UnauthorizedException();

    public async Task<ExecutionCapabilityDto> GetProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        var actions = await workflow.GetProjectActionsAsync(Actor, current.Roles, projectId, cancellationToken);
        var project = await db.Projects.AsNoTracking().Where(x => x.Id == projectId)
            .Select(x => new { x.Id, x.Status, x.RowVersion }).SingleAsync(cancellationToken);
        var active = project.Status == "ACTIVE";
        var manager = await IsProjectManager(projectId, cancellationToken);
        var execution = new[]
        {
            Action("create_task", manager && active, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED"),
            Action("create_milestone", manager && active, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED"),
            Action("reorder_milestones", manager && active, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED"),
            Action("create_progress_report", manager && active, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED"),
            Action("schedule_meeting", manager && active, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED")
        };
        return new("PROJECT", project.Id, project.Id, project.Status, Convert.ToBase64String(project.RowVersion),
            actions.Actions.Select(x => new ExecutionActionDto(x.Code, x.Allowed, x.Reasons)).Concat(execution).ToArray(), project.Status);
    }

    public async Task<ExecutionCapabilityDto> GetTaskAsync(long taskId, CancellationToken cancellationToken)
    {
        var row = await db.Tasks.AsNoTracking().Where(x => x.Id == taskId)
            .Select(x => new { x.Id, x.Status, x.ConcurrencyToken, ProjectId = x.Milestone.ProjectId, ProjectStatus = x.Milestone.Project.Status })
            .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Task", taskId);
        await RequireProjectAccess(row.ProjectId, cancellationToken);
        var active = row.ProjectStatus == "ACTIVE";
        var manager = await IsProjectManager(row.ProjectId, cancellationToken);
        var assignee = await tasks.IsTaskAssigneeAsync(taskId, Actor, cancellationToken)
            && await tasks.IsUserActiveTeamMemberAsync(row.ProjectId, Actor, cancellationToken);
        var mentor = await IsScopedMentor(taskId, row.ProjectId, cancellationToken);
        var writer = manager || assignee || mentor;
        var structural = manager;
        var statusAllowed = active && writer && row.Status is not "CANCELLED" and not "COMPLETED";
        var actions = new[]
        {
            Action("update_task", active && structural, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED"),
            Action("delete_task", active && structural, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED"),
            Action("assign_task", active && structural, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED"),
            Action("change_task_status", statusAllowed, !active ? "PROJECT_NOT_ACTIVE" : !writer ? "TASK_WRITER_REQUIRED" : "TASK_NOT_WRITABLE"),
            Action("manage_task_dependencies", active && structural, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED"),
            Action("manage_task_disciplines", active && writer, !active ? "PROJECT_NOT_ACTIVE" : "TASK_WRITER_REQUIRED"),
            Action("add_task_evidence", active && writer, !active ? "PROJECT_NOT_ACTIVE" : "TASK_WRITER_REQUIRED")
        };
        return new("TASK", row.Id, row.ProjectId, row.ProjectStatus, row.ConcurrencyToken.ToString("N"), actions, row.Status);
    }

    public async Task<ExecutionCapabilityDto> GetMilestoneAsync(long milestoneId, CancellationToken cancellationToken)
    {
        var row = await db.Milestones.AsNoTracking().Where(x => x.Id == milestoneId)
            .Select(x => new { x.Id, x.Status, x.ConcurrencyToken, x.ProjectId, ProjectStatus = x.Project.Status })
            .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Milestone", milestoneId);
        await RequireProjectAccess(row.ProjectId, cancellationToken);
        var active = row.ProjectStatus == "ACTIVE";
        var manager = await IsProjectManager(row.ProjectId, cancellationToken);
        var writable = active && manager && row.Status is not "CANCELLED" and not "COMPLETED";
        var reason = !active ? "PROJECT_NOT_ACTIVE" : !manager ? "PROJECT_MANAGER_REQUIRED" : "MILESTONE_NOT_WRITABLE";
        var actions = new[]
        {
            Action("update_milestone", writable, reason),
            Action("delete_milestone", writable, reason),
            Action("reorder_milestones", active && manager, !active ? "PROJECT_NOT_ACTIVE" : "PROJECT_MANAGER_REQUIRED")
        };
        return new("MILESTONE", row.Id, row.ProjectId, row.ProjectStatus, row.ConcurrencyToken.ToString("N"), actions, row.Status);
    }

    private async Task RequireProjectAccess(long projectId, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(x => x.Id == projectId, cancellationToken)) throw new NotFoundException("Project", projectId);
        if (!await access.CanAccessAsync(Actor, projectId, cancellationToken)) throw new ForbiddenException();
    }

    private Task<bool> IsProjectManager(long projectId, CancellationToken cancellationToken) =>
        tasks.IsProjectLeaderOrSupervisorAsync(projectId, Actor, cancellationToken);

    private async Task<bool> IsScopedMentor(long taskId, long projectId, CancellationToken cancellationToken)
    {
        if (!current.Roles.Contains(AppRoles.Lecturer, StringComparer.Ordinal)) return false;
        var majors = await db.TaskDisciplines.Where(x => x.TaskId == taskId).Select(x => x.MajorId).ToArrayAsync(cancellationToken);
        return majors.Length > 0 && await db.SupervisorAssignments.AnyAsync(x => x.ProjectId == projectId && x.EndedAt == null
            && x.AssignmentType == "DISCIPLINE_MENTOR" && x.MajorId.HasValue && majors.Contains(x.MajorId.Value)
            && x.SupervisorProfile.UserId == Actor && x.SupervisorProfile.User.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer), cancellationToken);
    }

    private static ExecutionActionDto Action(string code, bool allowed, string reason) =>
        new(code, allowed, allowed ? Array.Empty<string>() : new[] { reason });
}
