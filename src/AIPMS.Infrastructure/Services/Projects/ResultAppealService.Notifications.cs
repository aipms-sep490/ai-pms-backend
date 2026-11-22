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
        var projectInfo = await db.Projects.AsNoTracking().Where(p => p.Id == row.ProjectId)
            .Select(p => new { p.Code, p.Title }).FirstOrDefaultAsync(ct);
        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == Actor)
            .Select(u => u.FullName).FirstOrDefaultAsync(ct);
        var codeSuffix = string.IsNullOrWhiteSpace(projectInfo?.Code) ? "" : $" - Đồ án {projectInfo.Code}";
        var projectFull = projectInfo is null ? "của bạn" : string.IsNullOrWhiteSpace(projectInfo.Title) ? projectInfo.Code : $"{projectInfo.Code} - {projectInfo.Title}";
        var statusLabel = row.Status switch
        {
            "ACCEPTED" => "Chấp thuận",
            "REJECTED" => "Từ chối",
            _ => "Đã cập nhật"
        };
        var (rawTitle, content) = action switch
        {
            "APPEAL_REQUESTED" => (
                $"Yêu cầu phúc khảo điểm mới{codeSuffix}",
                $"{(string.IsNullOrWhiteSpace(actorName) ? "Sinh viên" : actorName)} đã gửi yêu cầu phúc khảo kết quả cho đồ án {projectFull}. Vui lòng đăng nhập để xem xét và phân công xử lý."),
            "APPEAL_REVIEWER_ASSIGNED" => (
                $"Phân công xem xét phúc khảo{codeSuffix}",
                $"Bạn đã được phân công xem xét yêu cầu phúc khảo kết quả cho đồ án {projectFull}. Vui lòng đăng nhập để thực hiện đánh giá."),
            "APPEAL_REVIEWED" => (
                $"Đã hoàn tất đánh giá phúc khảo{codeSuffix}",
                $"{(string.IsNullOrWhiteSpace(actorName) ? "Giảng viên phản biện" : actorName)} đã hoàn tất đánh giá yêu cầu phúc khảo kết quả cho đồ án {projectFull}. Vui lòng xem xét và ra quyết định cuối cùng."),
            "APPEAL_DECIDED" => (
                $"Kết quả phúc khảo{(string.IsNullOrWhiteSpace(projectInfo?.Code) ? "" : $" đồ án {projectInfo.Code}")}: {statusLabel}",
                $"Yêu cầu phúc khảo kết quả cho đồ án {projectFull} đã có quyết định: {statusLabel}. Vui lòng đăng nhập để xem chi tiết."),
            _ => (
                $"Cập nhật yêu cầu phúc khảo{codeSuffix}",
                $"Yêu cầu phúc khảo kết quả cho đồ án {projectFull} có cập nhật mới. Vui lòng đăng nhập để xem chi tiết.")
        };
        var safeTitle = rawTitle.Length <= 255 ? rawTitle : rawTitle[..255];
        var inbox = new M.Notification { CreatedBy = Actor, NotificationType = action, Title = safeTitle,
            Content = content, RelatedEntityType = "PROJECT", RelatedEntityId = row.ProjectId,
            CreatedAt = Now, UpdatedAt = Now, NotificationRecipients = recipients.Select(id => new M.NotificationRecipient {
                UserId = id, CreatedAt = Now, UpdatedAt = Now, DeliveredAt = Now }).ToArray() };
        db.Add(inbox); await db.SaveChangesAsync(ct);
        db.AddRange(inbox.NotificationRecipients.Select(r => new NotificationEmailDelivery { NotificationRecipientId = r.Id, NextAttemptAt = Now }));
        await db.SaveChangesAsync(ct);
    }
}
