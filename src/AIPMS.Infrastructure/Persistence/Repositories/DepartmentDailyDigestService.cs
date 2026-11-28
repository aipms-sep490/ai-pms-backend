using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Options;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using EmailDeliveryEntity = AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class DepartmentDailyDigestService(
    AipmsDbContext db,
    IOptions<DepartmentDigestSettings> options) : IDepartmentDailyDigestService
{
    public async Task<int> SendDigestAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var dayStartUtc = DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);
        var dayEndUtc = dayStartUtc.AddDays(1);
        var maxDepts = Math.Clamp(options.Value.BatchSize, 1, 500);

        var departments = await db.Departments.AsNoTracking()
            .Where(d => d.IsActive && d.Organization.IsActive)
            .OrderBy(d => d.Id)
            .Take(maxDepts)
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(ct);

        var emittedCount = 0;

        foreach (var dept in departments)
        {
            var alreadySent = await db.Notifications.AsNoTracking()
                .AnyAsync(n => n.NotificationType == "DEPARTMENT_DAILY_DIGEST"
                    && n.RelatedEntityType == "DEPARTMENT"
                    && n.RelatedEntityId == dept.Id
                    && n.CreatedAt >= dayStartUtc
                    && n.CreatedAt < dayEndUtc, ct);
            if (alreadySent)
                continue;

            var staffIds = await db.Users.AsNoTracking()
                .Where(u => u.Status == "ACTIVE"
                    && u.DepartmentId == dept.Id
                    && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff))
                .OrderBy(u => u.Id)
                .Select(u => u.Id)
                .ToArrayAsync(ct);
            if (staffIds.Length == 0)
                continue;

            var deptProjectIds = await db.ProjectMajors.AsNoTracking()
                .Where(pm => pm.Major.IsActive && pm.Major.DepartmentId == dept.Id)
                .Select(pm => pm.ProjectId)
                .Distinct()
                .ToArrayAsync(ct);

            if (deptProjectIds.Length == 0)
                continue;

            var pendingProposals = await db.Projects.AsNoTracking()
                .CountAsync(p => deptProjectIds.Contains(p.Id)
                    && (p.Status == "SUBMITTED" || p.Status == "UNDER_REVIEW"), ct);

            var openAppeals = await db.Set<ResultAppealRequest>().AsNoTracking()
                .CountAsync(r => r.ResponsibleDepartmentId == dept.Id
                    && r.Status != "ACCEPTED"
                    && r.Status != "REJECTED"
                    && r.Status != "WITHDRAWN", ct);

            var pendingGates = await db.ProjectGateInstances.AsNoTracking()
                .CountAsync(g => deptProjectIds.Contains(g.ProjectId)
                    && (g.Status == "IN_REVIEW" || g.Status == "REVISION_REQUIRED"), ct);

            var activeProjectIds = await db.Projects.AsNoTracking()
                .Where(p => deptProjectIds.Contains(p.Id)
                    && (p.Status == "ACTIVE" || p.Status == "FINAL_SUBMISSION"))
                .Select(p => p.Id)
                .ToArrayAsync(ct);

            var highRiskProjects = 0;
            if (activeProjectIds.Length > 0)
            {
                var latestRisks = await db.Set<RiskHistory>().AsNoTracking()
                    .Where(r => activeProjectIds.Contains(r.ProjectId))
                    .OrderByDescending(r => r.RecordedAt)
                    .ThenByDescending(r => r.Id)
                    .Select(r => new { r.ProjectId, r.RiskLevel })
                    .ToListAsync(ct);

                highRiskProjects = latestRisks
                    .GroupBy(r => r.ProjectId)
                    .Count(g => g.First().RiskLevel is "HIGH" or "CRITICAL" or "RED");
            }

            if (pendingProposals == 0 && openAppeals == 0 && pendingGates == 0 && highRiskProjects == 0)
                continue;

            var dateLabel = utcNow.AddHours(7).ToString("dd/MM/yyyy");
            var title = $"Tổng hợp vận hành hằng ngày ({dateLabel}) - {dept.Name}";
            if (title.Length > 255) title = title[..255];

            var content = $"Báo cáo tổng hợp {dept.Name} ngày {dateLabel}:\n"
                + $"• Hồ sơ đề xuất chờ duyệt: {pendingProposals}\n"
                + $"• Cổng kiểm duyệt (Gate) cần xử lý: {pendingGates}\n"
                + $"• Đồ án cảnh báo rủi ro cao: {highRiskProjects}\n"
                + $"• Đơn phúc khảo/đính chính đang mở: {openAppeals}";

            var notification = new M.Notification
            {
                CreatedBy = staffIds[0],
                NotificationType = "DEPARTMENT_DAILY_DIGEST",
                Title = title,
                Content = content,
                RelatedEntityType = "DEPARTMENT",
                RelatedEntityId = dept.Id,
                CreatedAt = utcNow,
                UpdatedAt = utcNow,
                NotificationRecipients = staffIds.Select(id => new M.NotificationRecipient
                {
                    UserId = id,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow,
                    DeliveredAt = utcNow
                }).ToArray()
            };

            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);

            db.Set<EmailDeliveryEntity>().AddRange(notification.NotificationRecipients.Select(r => new EmailDeliveryEntity
            {
                NotificationRecipientId = r.Id,
                NextAttemptAt = utcNow
            }));
            await db.SaveChangesAsync(ct);
            emittedCount++;
        }

        return emittedCount;
    }
}
