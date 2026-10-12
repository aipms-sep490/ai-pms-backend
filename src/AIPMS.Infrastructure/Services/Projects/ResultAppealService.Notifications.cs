using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ResultAppealService
{
    private async Task Notify(ResultAppealRequest row, string action, CancellationToken ct)
    {
        var project = (await transactions.GetProjectAsync(row.ProjectId, ct))!;
        var ids = new HashSet<long>();
        if (action is "APPEAL_REQUESTED" or "APPEAL_REVIEWED")
            ids.UnionWith(await db.Users.Where(u => u.Status == "ACTIVE" && u.DepartmentId == project.LeadDepartmentId
                && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF")).Select(u => u.Id).ToArrayAsync(ct));
        if (action == "APPEAL_REVIEWER_ASSIGNED" && row.ReviewerId is long reviewer) ids.Add(reviewer);
        if (action == "APPEAL_DECIDED")
        {
            ids.Add(row.RequestedBy);
            if (row.Status == "ACCEPTED") ids.UnionWith(await db.Set<ResultRevision>().Where(r => r.BundleId == row.BundleId && r.StudentId != null)
                .Select(r => r.StudentId!.Value).ToArrayAsync(ct));
        }
        ids.Remove(Actor);
        var recipients = await db.Users.Where(u => ids.Contains(u.Id) && u.Status == "ACTIVE").Select(u => u.Id).ToArrayAsync(ct);
        if (recipients.Length == 0) return;
        var inbox = new M.Notification { CreatedBy = Actor, NotificationType = action, Title = "A result review has an update",
            Content = "Sign in to view updates available to your account.", RelatedEntityType = "PROJECT", RelatedEntityId = row.ProjectId,
            CreatedAt = Now, UpdatedAt = Now, NotificationRecipients = recipients.Select(id => new M.NotificationRecipient {
                UserId = id, CreatedAt = Now, UpdatedAt = Now, DeliveredAt = Now }).ToArray() };
        db.Add(inbox); await db.SaveChangesAsync(ct);
        db.AddRange(inbox.NotificationRecipients.Select(r => new NotificationEmailDelivery { NotificationRecipientId = r.Id, NextAttemptAt = Now }));
        await db.SaveChangesAsync(ct);
    }
}
