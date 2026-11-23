using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

/// <summary>
/// Shared helper for emitting deduplicated in-app (+ optional email) workflow notifications
/// for V4.0 Core events without duplicating recipient lookup or email outbox logic.
/// </summary>
internal static class V4WorkflowNotificationHelper
{
    public static async Task EmitAsync(
        AipmsDbContext db,
        long actorId,
        string notificationType,
        string title,
        string content,
        string relatedEntityType,
        long relatedEntityId,
        IEnumerable<long> recipientIds,
        DateTime nowUtc,
        bool enqueueEmail,
        CancellationToken ct,
        bool excludeActor = true)
    {
        var distinctIds = recipientIds
            .Where(id => id > 0 && (!excludeActor || id != actorId))
            .Distinct()
            .ToArray();
        if (distinctIds.Length == 0) return;

        var activeIds = await db.Users.AsNoTracking()
            .Where(u => distinctIds.Contains(u.Id) && u.Status == "ACTIVE")
            .Select(u => u.Id)
            .ToArrayAsync(ct);
        if (activeIds.Length == 0) return;

        var notification = new M.Notification
        {
            CreatedBy = actorId,
            NotificationType = notificationType,
            Title = title,
            Content = content,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            NotificationRecipients = activeIds.Select(id => new M.NotificationRecipient
            {
                UserId = id,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                DeliveredAt = nowUtc
            }).ToArray()
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        if (!enqueueEmail) return;

        var savedRecipients = notification.NotificationRecipients.Where(r => r.Id > 0).ToArray();
        if (savedRecipients.Length == 0) return;

        db.Set<NotificationEmailDelivery>().AddRange(savedRecipients.Select(r => new NotificationEmailDelivery
        {
            NotificationRecipientId = r.Id,
            NextAttemptAt = nowUtc
        }));
        await db.SaveChangesAsync(ct);
    }

    public static async Task<long[]> GetActiveTeamStudentIdsAsync(AipmsDbContext db, long projectId, CancellationToken ct) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Team.TeamMembers.Where(m => m.LeftAt == null).Select(m => m.UserId))
            .Distinct()
            .ToArrayAsync(ct);

    public static async Task<long[]> GetActiveSupervisorAndMentorIdsAsync(AipmsDbContext db, long projectId, bool primaryOnly, CancellationToken ct) =>
        await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.EndedAt == null && (!primaryOnly || (a.IsPrimary && a.AssignmentType == "PRIMARY")))
            .Select(a => a.SupervisorProfile.UserId)
            .Distinct()
            .ToArrayAsync(ct);

    public static async Task<long[]> GetDepartmentStaffIdsAsync(AipmsDbContext db, long departmentId, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Status == "ACTIVE" && u.DepartmentId == departmentId
                && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"))
            .Select(u => u.Id)
            .Distinct()
            .ToArrayAsync(ct);
}
