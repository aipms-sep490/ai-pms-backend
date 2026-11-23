using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProjectHub;
using AIPMS.Application.Features.ProjectHub.Abstractions;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ProjectHubService
{
    public async Task<InternalContributionHeatmapDto> GetInternalHeatmapAsync(long projectId, int weeks, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var clampedWeeks = Math.Clamp(weeks, 1, 12);
        var now = Now;
        var windowStart = now.AddDays(-7 * clampedWeeks);

        var members = await (from tm in db.TeamMembers.AsNoTracking()
                             join u in db.Users.AsNoTracking() on tm.UserId equals u.Id
                             where tm.TeamId == ctx.Project.TeamId && tm.LeftAt == null
                             orderby u.FullName
                             select new { tm.UserId, u.FullName, u.MajorId }).ToArrayAsync(ct);
        var memberIds = members.Select(m => m.UserId).ToArray();

        // Collect purely internal activity timestamps (ZERO Git sync!)
        var taskCreated = await db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt == null && t.Milestone.ProjectId == projectId && memberIds.Contains(t.CreatedBy))
            .Select(t => new { UserId = t.CreatedBy, At = t.CreatedAt }).ToArrayAsync(ct);
        var statusChanges = await db.TaskStatusHistories.AsNoTracking()
            .Where(h => h.Task.DeletedAt == null && h.Task.Milestone.ProjectId == projectId && memberIds.Contains(h.ChangedBy))
            .Select(h => new { UserId = h.ChangedBy, At = h.ChangedAt }).ToArrayAsync(ct);
        var reports = await db.ProgressReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId && memberIds.Contains(r.SubmittedBy))
            .Select(r => new { UserId = r.SubmittedBy, At = r.SubmittedAt ?? r.UpdatedAt }).ToArrayAsync(ct);
        var gateEvidences = await db.Set<GateEvidence>().AsNoTracking()
            .Where(e => e.ProjectId == projectId && memberIds.Contains(e.SubmittedBy))
            .Select(e => new { UserId = e.SubmittedBy, At = e.SubmittedAt }).ToArrayAsync(ct);
        var chatMsgs = await (from m in db.Set<M.ChatMessage>().AsNoTracking()
                              join c in db.Set<M.ChatConversation>().AsNoTracking() on m.ConversationId equals c.Id
                              where c.ProjectId == projectId && memberIds.Contains(m.SenderId) && m.RecalledAt == null
                              select new { UserId = m.SenderId, At = m.CreatedAt }).ToArrayAsync(ct);

        var allEvents = taskCreated.Concat(statusChanges).Concat(reports).Concat(gateEvidences).Concat(chatMsgs)
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.At).ToArray());

        var rows = new List<MemberWeeklyActivityDto>(members.Length);
        var silentIds = new List<long>();
        foreach (var member in members)
        {
            var events = allEvents.GetValueOrDefault(member.UserId) ?? [];
            var buckets = new int[clampedWeeks];
            foreach (var at in events)
            {
                if (at < windowStart || at > now) continue;
                var daysAgo = (int)Math.Floor((now - at).TotalDays);
                var weekFromEnd = Math.Clamp(daysAgo / 7, 0, clampedWeeks - 1);
                var bucketIndex = (clampedWeeks - 1) - weekFromEnd;
                buckets[bucketIndex]++;
            }
            DateTime? lastAt = events.Length == 0 ? null : events.Max();
            int? daysSince = lastAt.HasValue ? (int)Math.Floor((now - lastAt.Value).TotalDays) : null;
            var isSilent = !lastAt.HasValue || (now - lastAt.Value).TotalDays > 10.0;
            if (isSilent) silentIds.Add(member.UserId);
            rows.Add(new(member.UserId, member.FullName, member.MajorId, buckets, buckets.Sum(), lastAt, daysSince, isSilent));
        }

        return new(projectId, clampedWeeks, now, rows, silentIds);
    }

    public async Task<EvidenceRubricLinkDto> LinkEvidenceToRubricAsync(long projectId, LinkEvidenceToRubricRequest input, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        if (!await db.Set<GateEvidence>().AsNoTracking().AnyAsync(e => e.Id == input.EvidenceId && e.ProjectId == projectId, ct))
            throw new NotFoundException("GateEvidence", input.EvidenceId);
        if (!await db.RubricCriteria.AsNoTracking().AnyAsync(c => c.Id == input.RubricCriterionId, ct))
            throw new NotFoundException("RubricCriterion", input.RubricCriterionId);

        var existing = await db.Set<EvidenceRubricCriterionLink>().AsNoTracking()
            .SingleOrDefaultAsync(l => l.EvidenceId == input.EvidenceId && l.RubricCriterionId == input.RubricCriterionId, ct);
        if (existing is not null)
            return new(existing.Id, existing.ProjectId, existing.EvidenceId, existing.RubricCriterionId, existing.CreatedBy, existing.CreatedAt);

        var link = new EvidenceRubricCriterionLink
        {
            ProjectId = projectId,
            EvidenceId = input.EvidenceId,
            RubricCriterionId = input.RubricCriterionId,
            CreatedBy = Actor,
            CreatedAt = Now
        };
        db.Set<EvidenceRubricCriterionLink>().Add(link);
        await db.SaveChangesAsync(ct);
        return new(link.Id, link.ProjectId, link.EvidenceId, link.RubricCriterionId, link.CreatedBy, link.CreatedAt);
    }

    public async Task<IReadOnlyList<EvidenceRubricLinkDto>> ListEvidenceRubricLinksAsync(long projectId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        return await db.Set<EvidenceRubricCriterionLink>().AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new EvidenceRubricLinkDto(l.Id, l.ProjectId, l.EvidenceId, l.RubricCriterionId, l.CreatedBy, l.CreatedAt))
            .ToArrayAsync(ct);
    }

    public async Task<ProjectHealthPulseDto> GetHealthPulseAsync(long projectId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireHealthPulseScope(ctx);
        var now = Now;

        var gateReqs = await db.ProjectGateRequirements.AsNoTracking()
            .Include(r => r.Evidence)
            .Where(r => r.GateInstance.ProjectId == projectId && r.Required && r.VerificationStatus != "WAIVED_WITHDRAWN")
            .ToArrayAsync(ct);
        var totalMandatory = gateReqs.Length;
        var readyCount = gateReqs.Count(r => r.Evidence.OrderByDescending(e => e.Version).FirstOrDefault()?.ScanStatus == "READY");
        decimal? evidenceReadyPct = totalMandatory == 0 ? null : decimal.Round(readyCount * 100m / totalMandatory, 2);

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt == null && t.Milestone.ProjectId == projectId)
            .Select(t => new { t.Status, t.BlockedSinceAt, t.TaskCategory }).ToArrayAsync(ct);
        var blockedCutoff = now.AddHours(-48);
        var blockedOver48h = tasks.Count(t => t.Status == "BLOCKED" && t.BlockedSinceAt.HasValue && t.BlockedSinceAt.Value <= blockedCutoff);

        var reportCutoff = now.AddDays(-14);
        var totalReports = await db.ProgressReports.AsNoTracking().CountAsync(r => r.ProjectId == projectId, ct);
        var recentReports = await db.ProgressReports.AsNoTracking()
            .CountAsync(r => r.ProjectId == projectId && r.Status != "DRAFT" && (r.SubmittedAt ?? r.UpdatedAt) >= reportCutoff, ct);

        decimal? balanceRatio = null;
        var isInter = string.Equals(ctx.Project.TrackCode, "IT_MKT", StringComparison.OrdinalIgnoreCase)
            || ctx.Project.ProjectMajors.Count > 1;
        if (isInter)
        {
            var itCount = tasks.Count(t => t.TaskCategory != null && t.TaskCategory.StartsWith("IT_", StringComparison.OrdinalIgnoreCase));
            var mktCount = tasks.Count(t => t.TaskCategory != null && t.TaskCategory.StartsWith("MKT_", StringComparison.OrdinalIgnoreCase));
            if (itCount + mktCount > 0)
            {
                var max = Math.Max(itCount, mktCount);
                var min = Math.Min(itCount, mktCount);
                balanceRatio = decimal.Round((decimal)min / max, 2);
            }
        }

        var signals = new HealthPulseSignalsDto(evidenceReadyPct, totalMandatory, blockedOver48h, recentReports, balanceRatio);

        string status;
        string summary;
        if (totalMandatory == 0 && tasks.Length == 0 && totalReports == 0)
        {
            status = "INSUFFICIENT_DATA";
            summary = "Chưa đủ tín hiệu vận hành (chưa có Gate, Task hoặc Báo cáo tuần) để kết luận sức khỏe đồ án.";
        }
        else if (blockedOver48h >= 2 || (evidenceReadyPct.HasValue && evidenceReadyPct.Value < 50m) || (balanceRatio.HasValue && balanceRatio.Value < 0.25m))
        {
            status = "RED";
            summary = "Đồ án có nguy cơ cao: tồn đọng Task BLOCKED > 48h, tỷ lệ minh chứng READY thấp hoặc lệch nhịp IT/MKT.";
        }
        else if (blockedOver48h == 1 || (evidenceReadyPct.HasValue && evidenceReadyPct.Value < 80m) || recentReports == 0 || (balanceRatio.HasValue && balanceRatio.Value < 0.50m))
        {
            status = "YELLOW";
            summary = "Đồ án cần theo dõi: kiểm tra báo cáo tuần, minh chứng còn thiếu hoặc tiến độ liên ngành.";
        }
        else
        {
            status = "GREEN";
            summary = "Tất cả 4 tín hiệu thực thi đang ổn định.";
        }

        return new(projectId, status, summary, signals, now);
    }

    public async Task<IReadOnlyList<ProjectAuditTimelineEntryDto>> GetAuditTimelineAsync(long projectId, int limit, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireAuditTimelineScope(ctx);
        var take = Math.Clamp(limit, 1, 200);
        var pid = projectId.ToString();
        var marker = $"\"projectId\":{projectId}";
        return await db.AuditLogs.AsNoTracking()
            .Where(a => (a.EntityType == "PROJECT" && a.EntityId == pid)
                || (a.DetailsJson != null && a.DetailsJson.Contains(marker)))
            .OrderByDescending(a => a.OccurredAt)
            .Take(take)
            .Select(a => new ProjectAuditTimelineEntryDto(a.Id, a.ActorUserId, a.Action, a.EntityType, a.EntityId, a.Outcome, a.OccurredAt))
            .ToArrayAsync(ct);
    }
}
