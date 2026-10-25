using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class RiskAlertService(
    AipmsDbContext db,
    IProjectProgressDataReader progressReader,
    IProgressAnalysisService analysis,
    IOptions<RiskAlertSettings> options)
    : IRiskAlertService
{
    public async Task<IReadOnlyList<long>> GetProjectIdsAsync(long afterId, int limit, CancellationToken ct) =>
        await db.Projects.AsNoTracking().Where(p => p.Id > afterId && p.Status == "ACTIVE")
            .OrderBy(p => p.Id).Select(p => p.Id).Take(limit).ToListAsync(ct);

    public async Task ProcessProjectAsync(long projectId, DateTime nowUtc, CancellationToken ct)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC is required.", nameof(nowUtc));

        var settings = options.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var project = await db.Projects.FromSqlInterpolated(
            $"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (project is null || project.Status != "ACTIVE") return;

        var scope = await db.Teams.Where(t => t.Id == project.TeamId).Select(t => new
        {
            SemesterId = t.AcademicSemesterId,
            t.AcademicSemester.OrganizationId,
            t.AcademicSemester.Status,
            t.AcademicSemester.StartDate,
            t.AcademicSemester.EndDate,
            t.AcademicSemester.Organization.IsActive
        }).SingleAsync(ct);

        var today = DateOnly.FromDateTime(nowUtc);
        if (!scope.IsActive || scope.Status != "ACTIVE" || today < scope.StartDate || today > scope.EndDate) return;

        var facts = await progressReader.GetProjectProgressFactsAsync(projectId, ct);
        if (facts is null || facts.Tasks.Count == 0 || facts.Milestones.Count == 0) return;

        var result = analysis.Analyze(facts, nowUtc, ct);
        if (result.DataStatus == "INSUFFICIENT_DATA" || result.RiskLevel == "INSUFFICIENT_DATA") return;

        var summary = result.ProgressSummary;
        var overdueTasks = summary?.OverdueTasks ?? 0;
        var blockedTasks = summary?.BlockedTasks ?? 0;
        var unassignedTasks = summary?.UnassignedTasks ?? 0;
        var progressPercent = summary?.ProgressPercentage ?? 0.0;
        var riskScore = result.RiskScore;

        var isElevated = result.RiskLevel is "MEDIUM" or "HIGH" or "CRITICAL"
            || overdueTasks >= settings.OverdueTaskThreshold
            || blockedTasks >= settings.BlockedTaskThreshold
            || (riskScore.HasValue && riskScore.Value >= settings.CriticalRiskScoreThreshold);

        if (!isElevated) return;

        // Record risk history snapshot for analytics
        var history = new RiskHistory
        {
            ProjectId = projectId,
            RiskLevel = result.RiskLevel,
            RiskScore = riskScore.HasValue ? (decimal?)Math.Round(riskScore.Value, 2) : null,
            OverdueTasks = overdueTasks,
            BlockedTasks = blockedTasks,
            UnassignedTasks = unassignedTasks,
            ProgressPercentage = (decimal)progressPercent,
            RecordedAt = nowUtc
        };
        db.Set<RiskHistory>().Add(history);

        // Project department scope
        var projectDepartmentIds = await db.ProjectMajors.Where(m => m.ProjectId == projectId && m.Major.IsActive)
            .Select(m => m.Major.DepartmentId).Distinct().ToListAsync(ct);

        var eligible = db.Users.Where(u => u.Status == "ACTIVE" && u.Department != null
            && u.Department.IsActive && u.Department.Organization.IsActive
            && u.Department.OrganizationId == scope.OrganizationId);

        // Supervisors and mentors
        var supervisors = await eligible.Where(u => u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
            && db.SupervisorAssignments.Any(a => a.ProjectId == projectId && a.EndedAt == null
                && a.SupervisorProfile.UserId == u.Id))
            .Select(u => u.Id).ToListAsync(ct);

        // Department staff in project department scope
        var staff = await eligible.Where(u => u.DepartmentId != null
            && projectDepartmentIds.Contains(u.DepartmentId.Value)
            && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"))
            .Select(u => u.Id).ToListAsync(ct);

        var recipients = supervisors.Concat(staff).Distinct().ToArray();
        if (recipients.Length == 0)
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return;
        }

        var key = FormattableString.Invariant($"RISK_ALERT:{projectId}:{nowUtc:yyyyMMdd}:{result.RiskLevel}");
        var occurrence = await db.Set<ScheduledNotificationOccurrence>()
            .Include(o => o.Notification).ThenInclude(n => n.NotificationRecipients)
            .SingleOrDefaultAsync(o => o.ProjectId == projectId && o.OccurrenceKey == key, ct);

        if (occurrence is null)
        {
            var title = FormattableString.Invariant($"Cảnh báo rủi ro tiến độ đồ án #{projectId}: Mức độ {result.RiskLevel}");
            var content = FormattableString.Invariant($"Đồ án có {overdueTasks} công việc quá hạn và {blockedTasks} công việc bị vướng (BLOCKED). Mức rủi ro: {result.RiskLevel}.");
            occurrence = new ScheduledNotificationOccurrence
            {
                ProjectId = projectId,
                OccurrenceKey = key,
                Notification = new M.Notification
                {
                    NotificationType = "PROJECT_RISK_ALERT",
                    Title = title,
                    Content = content,
                    RelatedEntityType = "PROJECT",
                    RelatedEntityId = projectId,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                }
            };
            db.Set<ScheduledNotificationOccurrence>().Add(occurrence);
        }

        foreach (var userId in recipients.Where(id => occurrence.Notification.NotificationRecipients.All(r => r.UserId != id)))
        {
            occurrence.Notification.NotificationRecipients.Add(new M.NotificationRecipient
            {
                UserId = userId,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                DeliveredAt = nowUtc
            });
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
