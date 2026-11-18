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
            var recipients = await db.Users.Where(u => u.Status == "ACTIVE" &&
                (db.TaskAssignees.Any(a => a.TaskId == task.Id && a.UserId == u.Id
                    && db.TeamMembers.Any(m => m.UserId == u.Id && m.LeftAt == null && m.Team.Project != null && m.Team.Project.Id == projectId))
                || db.TeamMembers.Any(m => m.UserId == u.Id && m.IsLeader && m.LeftAt == null
                    && m.Team.Project != null && m.Team.Project.Id == projectId)
                || db.SupervisorAssignments.Any(a => a.ProjectId == projectId && a.EndedAt == null
                    && a.IsPrimary && a.AssignmentType == "PRIMARY" && a.SupervisorProfile.UserId == u.Id)))
                .Select(u => u.Id).Distinct().ToArrayAsync(ct);
            if (recipients.Length == 0) continue;
            db.Notifications.Add(new M.Notification
            {
                CreatedBy = history.ChangedBy, NotificationType = "TASK_DEPENDENCY_IMPACT",
                Title = "Cần kiểm tra ảnh hưởng từ công việc tiền nhiệm",
                Content = $"Công việc #{task.Id} {task.Title} đã hoàn thành nhưng công việc tiền nhiệm #{predecessor.Id} {predecessor.Title} đã thay đổi sau hoàn thành. Lý do: {history.Reason}",
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
