using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using EmailDeliveryRow = AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class ScheduledNotificationService(
    AipmsDbContext db, IProjectProgressDataReader progressReader, IProgressAnalysisService analysis)
    : IScheduledNotificationService
{
    public async Task<IReadOnlyList<long>> GetProjectIdsAsync(long afterId, int limit, CancellationToken ct) =>
        await db.Projects.AsNoTracking().Where(p => p.Id > afterId && p.Status == "ACTIVE")
            .OrderBy(p => p.Id).Select(p => p.Id).Take(limit).ToListAsync(ct);

    public async Task ProcessProjectAsync(long projectId, DateTime nowUtc, TimeSpan reminderWindow, CancellationToken ct)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC is required.", nameof(nowUtc));
        if (reminderWindow <= TimeSpan.Zero || reminderWindow > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(reminderWindow));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Serialize competing workers and keep source state/eligibility stable until the inbox commits.
        var project = await db.Projects.FromSqlInterpolated(
            $"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (project is null || project.Status != "ACTIVE") return;
        var scope = await db.Teams.Where(t => t.Id == project.TeamId).Select(t => new
        {
            SemesterId = t.AcademicSemesterId, t.AcademicSemester.OrganizationId,
            t.AcademicSemester.Status, t.AcademicSemester.StartDate, t.AcademicSemester.EndDate,
            t.AcademicSemester.Organization.IsActive
        }).SingleAsync(ct);
        var today = DateOnly.FromDateTime(nowUtc);
        if (!scope.IsActive || scope.Status != "ACTIVE" || today < scope.StartDate || today > scope.EndDate) return;

        var eligible = db.Users.Where(u => u.Status == "ACTIVE" && u.Department != null
            && u.Department.IsActive && u.Department.Organization.IsActive
            && u.Department.OrganizationId == scope.OrganizationId);
        var students = await eligible.Where(u => u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")
            && u.TeamMembers.Any(m => m.TeamId == project.TeamId && m.LeftAt == null))
            .Select(u => u.Id).ToListAsync(ct);
        var supervisors = await eligible.Where(u => u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
            && db.SupervisorAssignments.Any(a => a.ProjectId == projectId && a.EndedAt == null && a.IsPrimary
                && a.SupervisorProfile.UserId == u.Id)
            && db.ProjectMajors.Any(m => m.ProjectId == projectId && m.Major.IsActive && m.Major.DepartmentId == u.DepartmentId))
            .Select(u => u.Id).ToListAsync(ct);
        var warningRecipients = students.Concat(supervisors).Distinct().ToArray();
        var horizon = nowUtc.Add(reminderWindow);

        var tasks = await db.Tasks.AsNoTracking().Where(t => t.Milestone.ProjectId == projectId
            && t.DeletedAt == null
            && t.Milestone.Status != "CANCELLED" && t.Milestone.Status != "COMPLETED"
            && (t.Status == "TODO" || t.Status == "IN_PROGRESS" || t.Status == "BLOCKED" || t.Status == "IN_REVIEW")
            && t.DueAt != null && t.DueAt <= horizon)
            .Select(t => new { t.Id, t.Title, DueAt = t.DueAt!.Value }).ToListAsync(ct);
        foreach (var task in tasks)
            await DeadlineAsync(project, "TASK", task.Id, task.Title, task.DueAt, task.DueAt < nowUtc,
                students, warningRecipients, nowUtc, ct);

        var deliverables = await db.Deliverables.AsNoTracking().Where(d => d.ProjectId == projectId
            && (d.Status == "DRAFT" || d.Status == "OPEN" || d.Status == "REJECTED")
            && (d.Milestone == null || (d.Milestone.Status != "CANCELLED" && d.Milestone.Status != "COMPLETED"))
            && d.DueAt != null && d.DueAt <= horizon
            && !d.DeliverableVersions.Any(v => v.Status == "SUBMITTED" || v.Status == "ACCEPTED"))
            .Select(d => new { d.Id, d.Title, DueAt = d.DueAt!.Value }).ToListAsync(ct);
        foreach (var deliverable in deliverables)
            await DeadlineAsync(project, "DELIVERABLE", deliverable.Id, deliverable.Title, deliverable.DueAt, deliverable.DueAt < nowUtc,
                students, warningRecipients, nowUtc, ct);

        if (!await db.Set<FinalSubmission>().AnyAsync(s => s.ProjectId == projectId, ct))
        {
            var periods = await db.ProjectPeriods.AsNoTracking().Where(p => p.AcademicSemesterId == scope.SemesterId
                && p.PeriodType == "FINAL_SUBMISSION" && p.Status == "ACTIVE" && p.StartAt <= nowUtc)
                .OrderByDescending(p => p.StartAt).ThenByDescending(p => p.Id).ToListAsync(ct);
            var open = periods.Where(p => nowUtc < p.EndAt).ToArray();
            // Never guess a deadline when active submission windows overlap.
            var period = open.Length == 1 ? open[0] : open.Length == 0 ? periods.FirstOrDefault() : null;
            if (period is not null && period.EndAt <= horizon)
                await DeadlineAsync(project, "FINAL_SUBMISSION", period.Id, null, period.EndAt, period.EndAt <= nowUtc,
                    students, warningRecipients, nowUtc, ct);
        }

        var facts = await progressReader.GetProjectProgressFactsAsync(projectId, ct);
        if (facts is not null)
        {
            var risk = analysis.Analyze(facts, nowUtc, ct);
            // BE-06 may expose a strong blocker signal even when other features are incomplete.
            if (risk.RiskLevel is "MEDIUM" or "HIGH" or "CRITICAL")
            {
                var riskLabel = RiskLevelLabel(risk.RiskLevel);
                var riskTitle = Truncate($"Cảnh báo rủi ro tiến độ đồ án {project.Code}: Mức độ {riskLabel}");
                var riskContent = $"Đồ án {FormatProject(project)} đang ở mức rủi ro tiến độ {riskLabel}. Vui lòng kiểm tra tiến độ công việc và các mốc quan trọng.";
                await PublishAsync(projectId, $"RISK:{nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}:{risk.RiskLevel}",
                    "PROJECT_RISK_" + risk.RiskLevel, riskTitle, riskContent,
                    "PROJECT", projectId, warningRecipients, nowUtc, ct);
            }
        }
        await db.SaveChangesAsync(ct);
        await QueueEmailsAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task QueueEmailsAsync(CancellationToken ct)
    {
        // Queue every in-app notification created in this transaction; the email worker
        // remains independently opt-in and can retry without changing inbox state.
        var notificationIds = db.Notifications.Local
            .Select(n => n.Id)
            .Where(id => id > 0)
            .ToArray();
        if (notificationIds.Length == 0) return;
        var recipientIds = await db.NotificationRecipients.Where(r => notificationIds.Contains(r.NotificationId))
            .Select(r => r.Id).ToListAsync(ct);
        var existing = await db.Set<EmailDeliveryRow>().Where(d => recipientIds.Contains(d.NotificationRecipientId))
            .Select(d => d.NotificationRecipientId).ToListAsync(ct);
        db.Set<EmailDeliveryRow>().AddRange(recipientIds.Where(id => !existing.Contains(id))
            .Select(id => new EmailDeliveryRow { NotificationRecipientId = id, NextAttemptAt = DateTime.UtcNow }));
        await db.SaveChangesAsync(ct);
    }

    private Task DeadlineAsync(M.Project project, string entity, long sourceId, string? entityTitle, DateTime deadline, bool overdue,
        IReadOnlyList<long> students, IReadOnlyList<long> warningRecipients, DateTime now, CancellationToken ct)
    {
        var type = entity + (overdue ? "_OVERDUE" : "_DEADLINE_REMINDER");
        var key = FormattableString.Invariant($"{type}:{sourceId}:{deadline.Ticks}");
        var dueVn = DateTime.SpecifyKind(deadline, DateTimeKind.Utc).AddHours(7).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var projectFull = FormatProject(project);
        var cleanTitle = string.IsNullOrWhiteSpace(entityTitle) ? null : entityTitle.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var (title, content) = entity switch
        {
            "TASK" => overdue
                ? (cleanTitle is null ? $"Công việc quá hạn ({project.Code})" : $"Công việc quá hạn ({project.Code}): {cleanTitle}",
                    cleanTitle is null
                        ? $"Một công việc trong đồ án {projectFull} đã quá hạn (hạn chót: {dueVn}, giờ Việt Nam)."
                        : $"Công việc \"{cleanTitle}\" trong đồ án {projectFull} đã quá hạn (hạn chót: {dueVn}, giờ Việt Nam).")
                : (cleanTitle is null ? $"Công việc sắp đến hạn ({project.Code})" : $"Công việc sắp đến hạn ({project.Code}): {cleanTitle}",
                    cleanTitle is null
                        ? $"Một công việc trong đồ án {projectFull} sắp đến hạn (hạn chót: {dueVn}, giờ Việt Nam)."
                        : $"Công việc \"{cleanTitle}\" trong đồ án {projectFull} sắp đến hạn (hạn chót: {dueVn}, giờ Việt Nam)."),
            "DELIVERABLE" => overdue
                ? (cleanTitle is null ? $"Sản phẩm bàn giao quá hạn ({project.Code})" : $"Sản phẩm bàn giao quá hạn ({project.Code}): {cleanTitle}",
                    cleanTitle is null
                        ? $"Một sản phẩm bàn giao của đồ án {projectFull} đã quá hạn nộp (hạn chót: {dueVn}, giờ Việt Nam)."
                        : $"Sản phẩm bàn giao \"{cleanTitle}\" của đồ án {projectFull} đã quá hạn nộp (hạn chót: {dueVn}, giờ Việt Nam).")
                : (cleanTitle is null ? $"Sản phẩm bàn giao sắp đến hạn ({project.Code})" : $"Sản phẩm bàn giao sắp đến hạn ({project.Code}): {cleanTitle}",
                    cleanTitle is null
                        ? $"Một sản phẩm bàn giao của đồ án {projectFull} sắp đến hạn nộp (hạn chót: {dueVn}, giờ Việt Nam)."
                        : $"Sản phẩm bàn giao \"{cleanTitle}\" của đồ án {projectFull} sắp đến hạn nộp (hạn chót: {dueVn}, giờ Việt Nam)."),
            _ => overdue
                ? ($"Hồ sơ bàn giao cuối kỳ quá hạn ({project.Code})",
                    $"Hạn nộp hồ sơ bàn giao cuối kỳ của đồ án {projectFull} đã qua (hạn chót: {dueVn}, giờ Việt Nam).")
                : ($"Hồ sơ bàn giao cuối kỳ sắp đến hạn ({project.Code})",
                    $"Hồ sơ bàn giao cuối kỳ của đồ án {projectFull} sắp đến hạn nộp (hạn chót: {dueVn}, giờ Việt Nam).")
        };
        return PublishAsync(project.Id, key, type, Truncate(title), content, entity == "FINAL_SUBMISSION" ? "PROJECT" : entity,
            entity == "FINAL_SUBMISSION" ? project.Id : sourceId, overdue ? warningRecipients : students, now, ct);
    }

    private async Task PublishAsync(long projectId, string key, string type, string title, string content, string entity,
        long entityId, IReadOnlyList<long> recipients, DateTime now, CancellationToken ct)
    {
        if (recipients.Count == 0) return;
        var occurrence = await db.Set<ScheduledNotificationOccurrence>()
            .Include(o => o.Notification).ThenInclude(n => n.NotificationRecipients)
            .SingleOrDefaultAsync(o => o.ProjectId == projectId && o.OccurrenceKey == key, ct);
        if (occurrence is null)
        {
            occurrence = new() { ProjectId = projectId, OccurrenceKey = key, Notification = new M.Notification
            {
                NotificationType = type, Title = Truncate(title), Content = content, RelatedEntityType = entity,
                RelatedEntityId = entityId, CreatedAt = now, UpdatedAt = now
            } };
            db.Set<ScheduledNotificationOccurrence>().Add(occurrence);
        }
        foreach (var userId in recipients.Where(id => occurrence.Notification.NotificationRecipients.All(r => r.UserId != id)))
            occurrence.Notification.NotificationRecipients.Add(new M.NotificationRecipient
            {
                UserId = userId, CreatedAt = now, UpdatedAt = now, DeliveredAt = now
            });
    }

    private static string FormatProject(M.Project project) =>
        string.IsNullOrWhiteSpace(project.Title) ? project.Code : $"{project.Code} - {project.Title}";

    private static string RiskLevelLabel(string riskLevel) => riskLevel switch
    {
        "CRITICAL" => "Nghiêm trọng",
        "HIGH" => "Cao",
        "MEDIUM" => "Trung bình",
        "LOW" => "Thấp",
        _ => "Cần theo dõi"
    };

    private static string Truncate(string value, int maxLength = 255) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
