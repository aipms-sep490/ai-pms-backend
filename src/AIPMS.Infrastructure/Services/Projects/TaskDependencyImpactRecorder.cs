using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class TaskDependencyImpactRecorder
{
    // Stage impacts and inbox entries with the status change so one transaction owns all three.
    internal static async System.Threading.Tasks.Task StageAsync(AipmsDbContext db, M.Task predecessor,
        M.TaskStatusHistory history, CancellationToken ct)
    {
        var successors = await db.TaskDependencies
            .Where(d => d.DependsOnTaskId == predecessor.Id && d.DependencyType == "FINISH_TO_START"
                && d.Task.Status == "DONE")
            .Select(d => d.Task).Distinct().ToListAsync(ct);
        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == history.ChangedBy)
            .Select(u => u.FullName).FirstOrDefaultAsync(ct);
        foreach (var task in successors)
        {
            db.TaskDependencyImpacts.Add(new TaskDependencyImpact
            {
                TaskId = task.Id, PredecessorTaskId = predecessor.Id, ReopenHistory = history,
                ReopenedAt = history.ChangedAt, ReopenedBy = history.ChangedBy,
                ReopenReason = history.Reason!, Status = "PENDING"
            });
            task.ConcurrencyToken = Guid.NewGuid();
            task.UpdatedAt = history.ChangedAt;
            var projectId = await db.Milestones.Where(m => m.Id == task.MilestoneId).Select(m => m.ProjectId).SingleAsync(ct);
            var projectCode = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.Code).FirstOrDefaultAsync(ct);
            var recipients = await db.Users.Where(u => u.Status == "ACTIVE" &&
                (db.TaskAssignees.Any(a => a.TaskId == task.Id && a.UserId == u.Id
                    && db.TeamMembers.Any(m => m.UserId == u.Id && m.LeftAt == null && m.Team.Project != null && m.Team.Project.Id == projectId))
                || db.TeamMembers.Any(m => m.UserId == u.Id && m.IsLeader && m.LeftAt == null
                    && m.Team.Project != null && m.Team.Project.Id == projectId)
                || db.SupervisorAssignments.Any(a => a.ProjectId == projectId && a.EndedAt == null
                    && a.IsPrimary && a.AssignmentType == "PRIMARY" && a.SupervisorProfile.UserId == u.Id)))
                .Select(u => u.Id).Distinct().ToArrayAsync(ct);
            if (recipients.Length == 0) continue;
            var rawTitle = $"Cần kiểm tra ảnh hưởng công việc: {task.Title}";
            var safeTitle = rawTitle.Length <= 255 ? rawTitle : rawTitle[..255];
            var projectClause = string.IsNullOrWhiteSpace(projectCode) ? "" : $" (đồ án {projectCode})";
            var actorClause = string.IsNullOrWhiteSpace(actorName) ? "" : $" bởi {actorName}";
            db.Notifications.Add(new M.Notification
            {
                CreatedBy = history.ChangedBy, NotificationType = "TASK_DEPENDENCY_IMPACT",
                Title = safeTitle,
                Content = $"Công việc \"{task.Title}\"{projectClause} đã hoàn thành nhưng công việc tiền nhiệm \"{predecessor.Title}\" vừa thay đổi sau hoàn thành{actorClause}. Lý do: {history.Reason}",
                RelatedEntityType = "TASK", RelatedEntityId = task.Id,
                CreatedAt = history.ChangedAt, UpdatedAt = history.ChangedAt,
                NotificationRecipients = recipients.Select(id => new M.NotificationRecipient
                {
                    UserId = id, CreatedAt = history.ChangedAt, UpdatedAt = history.ChangedAt
                }).ToArray()
            });
        }
    }
}
