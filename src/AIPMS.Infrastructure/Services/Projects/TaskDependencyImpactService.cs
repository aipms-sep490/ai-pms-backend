using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Services.Projects;

public sealed class TaskDependencyImpactService(AipmsDbContext db, ICurrentUser current,
    IProjectAccessService access, IProjectExecutionGuard guard, ITaskRepository tasks,
    IAuditTrail audit, TimeProvider clock) : ITaskDependencyImpactService
{
    private long Actor => current.IsAuthenticated && current.UserId is long id ? id : throw new UnauthorizedException();

    public async Task<TaskDependencyImpactsDto> GetAsync(long taskId, CancellationToken ct)
    {
        var actor = Actor;
        var task = await db.Tasks.AsNoTracking().Where(t => t.Id == taskId)
            .Select(t => new { t.Milestone.ProjectId, ProjectStatus = t.Milestone.Project.Status }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Task", taskId);
        if (!await access.CanAccessAsync(actor, task.ProjectId, ct)) throw new ForbiddenException();
        var manager = await ProjectExecutionAuthority.IsManagerAsync(db, task.ProjectId, actor, ct);
        var canReview = task.ProjectStatus == "ACTIVE" && manager;
        var whole = manager || await db.Users.AnyAsync(u => u.Id == actor && u.Status == "ACTIVE"
            && (u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN")
                || u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT") && db.TeamMembers.Any(m => m.UserId == actor
                    && m.LeftAt == null && m.Team.Project != null && m.Team.Project.Id == task.ProjectId)), ct);
        AiScope? scoped = null;
        if (!whole)
        {
            scoped = await new AiActorScope(db, current).Read(task.ProjectId, ct);
            whole = scoped.Whole;
        }
        var majors = scoped?.Majors ?? [];
        if (!whole && !await db.TaskDisciplines.AnyAsync(d => d.TaskId == taskId && majors.Contains(d.MajorId), ct))
            throw new ForbiddenException("Công việc nằm ngoài ngành được phân công.");
        // An impact exposes its predecessor's title and reason, so both ends must be readable.
        var rows = await db.TaskDependencyImpacts.AsNoTracking().Where(x => x.TaskId == taskId
                && (whole || db.TaskDisciplines.Any(d => d.TaskId == x.PredecessorTaskId && majors.Contains(d.MajorId))))
            .OrderByDescending(x => x.Id)
            .Select(x => new TaskDependencyImpactDto(x.Id, x.TaskId, x.PredecessorTaskId,
                x.PredecessorTask.Title, x.PredecessorTask.Status, x.Status, x.ReopenedAt, x.ReopenedBy,
                x.ReopenReason, x.ReviewedAt, x.ReviewedBy, x.ReviewReason,
                canReview && x.Status == "PENDING" && (x.PredecessorTask.Status == "DONE" || x.PredecessorTask.Status == "CANCELLED"),
                x.Status != "PENDING" ? null : x.PredecessorTask.Status != "DONE" && x.PredecessorTask.Status != "CANCELLED"
                    ? "Cần hoàn tất hoặc xác nhận hủy công việc tiền nhiệm trước khi kết luận ảnh hưởng."
                    : !canReview ? "Chỉ trưởng nhóm hoặc giảng viên hướng dẫn chính được kết luận khi đồ án đang thực hiện." : null,
                null, null))
            .ToListAsync(ct);
        if (rows.Count > 0)
        {
            var userIds = rows.Select(x => x.ReopenedBy)
                .Concat(rows.Where(x => x.ReviewedBy.HasValue).Select(x => x.ReviewedBy!.Value))
                .Distinct().ToArray();
            var userNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                rows[i] = r with
                {
                    ReopenedByName = userNames.GetValueOrDefault(r.ReopenedBy),
                    ReviewedByName = r.ReviewedBy.HasValue ? userNames.GetValueOrDefault(r.ReviewedBy.Value) : null
                };
            }
        }
        if (scoped is not null && (await new AiActorScope(db, current).Read(task.ProjectId, ct)).Hash != scoped.Hash)
            throw new ForbiddenException("Phân công đã thay đổi; hãy tải lại trước khi xem.");
        return new(taskId, canReview, rows);
    }

    public async Task<TaskDto> ResolveAsync(long taskId, long impactId, string decision, string reason, CancellationToken ct)
    {
        var actor = Actor;
        var task = await db.Tasks.Include(t => t.Milestone).SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task", taskId);
        await guard.MustBeActiveForTaskAsync(taskId, ct);
        if (!await access.CanAccessAsync(actor, task.Milestone.ProjectId, ct)
            || !await ProjectExecutionAuthority.IsManagerAsync(db, task.Milestone.ProjectId, actor, ct))
            throw new ForbiddenException("Chỉ trưởng nhóm hoặc giảng viên hướng dẫn chính được kết luận ảnh hưởng.");
        if (decision is not ("CONFIRMED_VALID" or "REWORK_REQUIRED") || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new ConflictException("Chọn kết luận hợp lệ và nhập lý do từ 1 đến 1.000 ký tự.");
        var impact = await db.TaskDependencyImpacts.Include(x => x.PredecessorTask)
            .SingleOrDefaultAsync(x => x.Id == impactId && x.TaskId == taskId, ct)
            ?? throw new NotFoundException("Task dependency impact", impactId);
        if (impact.Status != "PENDING") throw new ConflictException("Ảnh hưởng đã được kết luận. Hãy tải lại công việc.");
        if (impact.PredecessorTask.Status is not ("DONE" or "CANCELLED"))
            throw new ConflictException("Cần hoàn tất hoặc xác nhận hủy công việc tiền nhiệm trước khi kết luận ảnh hưởng.", WorkflowErrorCodes.TaskDependencyBlocked);
        if (decision == "REWORK_REQUIRED" && task.Status is not ("DONE" or "IN_PROGRESS" or "TODO" or "BLOCKED"))
            throw new ConflictException("Không thể yêu cầu làm lại ở trạng thái hiện tại. Hãy xử lý bước nghiệm thu hoặc khôi phục công việc trước.");
        if (decision == "REWORK_REQUIRED" && task.Status == "DONE")
        {
            var blockers = await tasks.GetBlockingDependenciesAsync(taskId, ct);
            if (blockers.Count > 0)
                throw new ConflictException("Hoàn tất các công việc tiền nhiệm trước khi mở lại công việc phụ thuộc.", WorkflowErrorCodes.TaskDependencyBlocked);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var oldStatus = task.Status;
        impact.Status = decision;
        impact.ReviewedBy = actor;
        impact.ReviewedAt = now;
        impact.ReviewReason = reason.Trim();
        task.ConcurrencyToken = Guid.NewGuid();
        task.UpdatedAt = now;
        if (decision == "REWORK_REQUIRED" && oldStatus == "DONE")
            await tasks.UpdateStatusAsync(taskId, "IN_PROGRESS", reason.Trim(), actor, ct);
        else
            await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actor, "TASK_DEPENDENCY_IMPACT_RESOLVED", "TASK", taskId,
            new Dictionary<string, object?>
            {
                ["projectId"] = task.Milestone.ProjectId, ["impactId"] = impact.Id,
                ["predecessorTaskId"] = impact.PredecessorTaskId, ["decision"] = decision,
                ["reason"] = reason.Trim(), ["oldStatus"] = oldStatus, ["newStatus"] = task.Status,
                ["assigneeUserIds"] = await db.TaskAssignees.Where(x => x.TaskId == taskId).OrderBy(x => x.UserId).Select(x => x.UserId).ToArrayAsync(ct)
            }), ct);
        return (await tasks.GetByIdAsync(taskId, ct))!;
    }
}
