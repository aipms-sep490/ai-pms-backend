using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class ScheduleNotificationQueue
{
    public static async Task Gate3(AipmsDbContext db, long actor, long projectId, long gateId,
        long scriptId, DateTime now, bool changed, CancellationToken ct)
    {
        var students = await db.TeamMembers.AsNoTracking().Where(m => m.Team.Project != null
            && m.Team.Project.Id == projectId && m.LeftAt == null).Select(m => m.UserId).ToArrayAsync(ct);
        var reviewers = await db.GateReviewerAssignments.AsNoTracking().Where(a => a.GateInstanceId == gateId
            && a.Status != "REVOKED").Select(a => a.ReviewerId).ToArrayAsync(ct);
        var supervisors = await db.SupervisorAssignments.AsNoTracking().Where(a => a.ProjectId == projectId
            && a.EndedAt == null).Select(a => a.SupervisorProfile.UserId).ToArrayAsync(ct);
        await Queue(db, actor, students.Concat(reviewers).Concat(supervisors).Distinct().ToArray(),
            changed ? "REVIEW_GATE_3_SCHEDULE_CHANGED" : "REVIEW_GATE_3_SCHEDULED",
            changed ? "Lịch Review Gate 3 đã thay đổi" : "Lịch Review Gate 3 đã được công bố",
            "Xem lịch Review Gate 3 trong dự án để biết thời gian và phòng.", "GATE_SESSION_SCRIPT",
            scriptId, now, ct);
    }

    public static async Task Defense(AipmsDbContext db, long actor, long projectId,
        long committeeId, long sessionId, DateTime now, bool changed, CancellationToken ct)
    {
        var students = await db.TeamMembers.AsNoTracking().Where(m => m.Team.Project != null
            && m.Team.Project.Id == projectId && m.LeftAt == null).Select(m => m.UserId).ToArrayAsync(ct);
        var members = await db.Set<DefenseCommitteeMember>().AsNoTracking().Where(m => m.CommitteeId == committeeId)
            .Select(m => m.UserId).ToArrayAsync(ct);
        var supervisors = await db.SupervisorAssignments.AsNoTracking().Where(a => a.ProjectId == projectId
            && a.EndedAt == null).Select(a => a.SupervisorProfile.UserId).ToArrayAsync(ct);
        await Queue(db, actor, students.Concat(members).Concat(supervisors).Distinct().ToArray(),
            changed ? "DEFENSE_SCHEDULE_CHANGED" : "DEFENSE_SCHEDULED",
            changed ? "Lịch bảo vệ đã thay đổi" : "Lịch bảo vệ đã được công bố",
            "Xem lịch bảo vệ trong dự án để biết thời gian và phòng.", "DEFENSE_SESSION",
            sessionId, now, ct);
    }

    private static async Task Queue(AipmsDbContext db, long actor, long[] recipientIds,
        string type, string title, string content, string entityType, long entityId, DateTime now, CancellationToken ct)
    {
        var active = await db.Users.AsNoTracking().Where(u => recipientIds.Contains(u.Id) && u.Status == "ACTIVE")
            .Select(u => u.Id).ToArrayAsync(ct);
        if (active.Length == 0) return;
        var notification = new M.Notification { CreatedBy = actor, NotificationType = type,
            Title = title, Content = content, RelatedEntityType = entityType, RelatedEntityId = entityId,
            CreatedAt = now, UpdatedAt = now,
            NotificationRecipients = active.Select(id => new M.NotificationRecipient { UserId = id,
                CreatedAt = now, UpdatedAt = now, DeliveredAt = now }).ToArray() };
        db.Add(notification); await db.SaveChangesAsync(ct);
        db.AddRange(notification.NotificationRecipients.Select(r => new NotificationEmailDelivery {
            NotificationRecipientId = r.Id, NextAttemptAt = now }));
        await db.SaveChangesAsync(ct);
    }
}
