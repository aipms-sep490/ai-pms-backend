using System.Threading.Tasks;
using AIPMS.Application.Features.ProjectHub;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

/// <summary>
/// Authoritative 4-signal Health Pulse calculator (Spec V4:279 / TK-W2-13):
/// 1. % mandatory Gate requirements whose latest evidence is READY
/// 2. Count of non-deleted, non-CANCELLED tasks BLOCKED > 48h
/// 3. Count of submitted (!= DRAFT) progress reports in last 14 days
/// 4. IT/MKT task workload balance ratio (interdisciplinary IT_MKT projects only)
/// </summary>
internal sealed class ProjectHealthPulseCalculator(AipmsDbContext db) : IProjectHealthPulseCalculator
{
    public async Task<ProjectHealthPulseDto> CalculateAsync(long projectId, DateTime nowUtc, CancellationToken ct)
    {
        var map = await CalculateBatchAsync([projectId], nowUtc, ct);
        return map.TryGetValue(projectId, out var pulse)
            ? pulse
            : BuildInsufficient(projectId, nowUtc);
    }

    public async Task<IReadOnlyDictionary<long, ProjectHealthPulseDto>> CalculateBatchAsync(
        IReadOnlyList<long> projectIds,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var distinctIds = projectIds.Distinct().ToArray();
        if (distinctIds.Length == 0)
            return new Dictionary<long, ProjectHealthPulseDto>();

        var projectMeta = await db.Projects.AsNoTracking()
            .Where(p => distinctIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.TrackCode,
                MajorCount = p.ProjectMajors.Count
            })
            .ToDictionaryAsync(p => p.Id, ct);

        var gateReqs = await db.ProjectGateRequirements.AsNoTracking()
            .Include(r => r.Evidence)
            .Where(r => distinctIds.Contains(r.GateInstance.ProjectId)
                && r.Required
                && r.VerificationStatus != "WAIVED_WITHDRAWN")
            .Select(r => new
            {
                r.GateInstance.ProjectId,
                LatestScanStatus = r.Evidence
                    .OrderByDescending(e => e.Version)
                    .Select(e => e.ScanStatus)
                    .FirstOrDefault()
            })
            .ToArrayAsync(ct);

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt == null
                && t.Status != "CANCELLED"
                && distinctIds.Contains(t.Milestone.ProjectId))
            .Select(t => new
            {
                t.Milestone.ProjectId,
                t.Status,
                t.BlockedSinceAt,
                t.TaskCategory
            })
            .ToArrayAsync(ct);

        var reportCutoff = nowUtc.AddDays(-14);
        var reports = await db.ProgressReports.AsNoTracking()
            .Where(r => distinctIds.Contains(r.ProjectId))
            .Select(r => new
            {
                r.ProjectId,
                r.Status,
                EffectiveAt = r.SubmittedAt ?? r.UpdatedAt
            })
            .ToArrayAsync(ct);

        var gateReqsByProject = gateReqs.ToLookup(x => x.ProjectId);
        var tasksByProject = tasks.ToLookup(x => x.ProjectId);
        var reportsByProject = reports.ToLookup(x => x.ProjectId);
        var blockedCutoff = nowUtc.AddHours(-48);

        var result = new Dictionary<long, ProjectHealthPulseDto>(distinctIds.Length);
        foreach (var projectId in distinctIds)
        {
            var projReqs = gateReqsByProject[projectId].ToArray();
            var totalMandatory = projReqs.Length;
            var readyCount = projReqs.Count(r => r.LatestScanStatus == "READY");
            decimal? evidenceReadyPct = totalMandatory == 0
                ? null
                : decimal.Round(readyCount * 100m / totalMandatory, 2);

            var projTasks = tasksByProject[projectId].ToArray();
            var blockedOver48h = projTasks.Count(t =>
                t.Status == "BLOCKED"
                && t.BlockedSinceAt.HasValue
                && t.BlockedSinceAt.Value <= blockedCutoff);

            var projReports = reportsByProject[projectId].ToArray();
            var totalReports = projReports.Length;
            var recentReports = projReports.Count(r => r.Status != "DRAFT" && r.EffectiveAt >= reportCutoff);

            decimal? balanceRatio = null;
            var isInter = projectMeta.TryGetValue(projectId, out var meta)
                && (string.Equals(meta.TrackCode, "IT_MKT", StringComparison.OrdinalIgnoreCase) || meta.MajorCount > 1);
            if (isInter)
            {
                var itCount = projTasks.Count(t => t.TaskCategory != null && t.TaskCategory.StartsWith("IT_", StringComparison.OrdinalIgnoreCase));
                var mktCount = projTasks.Count(t => t.TaskCategory != null && t.TaskCategory.StartsWith("MKT_", StringComparison.OrdinalIgnoreCase));
                if (itCount + mktCount > 0)
                {
                    var max = Math.Max(itCount, mktCount);
                    var min = Math.Min(itCount, mktCount);
                    balanceRatio = decimal.Round((decimal)min / max, 2);
                }
            }

            var signals = new HealthPulseSignalsDto(
                evidenceReadyPct, totalMandatory, blockedOver48h, recentReports, balanceRatio);

            string status;
            string summary;
            if (totalMandatory == 0 && projTasks.Length == 0 && totalReports == 0)
            {
                status = "INSUFFICIENT_DATA";
                summary = "Chưa đủ tín hiệu vận hành (chưa có Gate, Task hoặc Báo cáo tuần) để kết luận sức khỏe đồ án.";
            }
            else if (blockedOver48h >= 2
                || (evidenceReadyPct.HasValue && evidenceReadyPct.Value < 50m)
                || (balanceRatio.HasValue && balanceRatio.Value < 0.25m))
            {
                status = "RED";
                summary = "Đồ án có nguy cơ cao: tồn đọng Task BLOCKED > 48h, tỷ lệ minh chứng READY thấp hoặc lệch nhịp IT/MKT.";
            }
            else if (blockedOver48h == 1
                || (evidenceReadyPct.HasValue && evidenceReadyPct.Value < 80m)
                || recentReports == 0
                || (balanceRatio.HasValue && balanceRatio.Value < 0.50m))
            {
                status = "YELLOW";
                summary = "Đồ án cần theo dõi: kiểm tra báo cáo tuần, minh chứng còn thiếu hoặc tiến độ liên ngành.";
            }
            else
            {
                status = "GREEN";
                summary = "Tất cả 4 tín hiệu thực thi đang ổn định.";
            }

            result[projectId] = new ProjectHealthPulseDto(projectId, status, summary, signals, nowUtc);
        }

        return result;
    }

    private static ProjectHealthPulseDto BuildInsufficient(long projectId, DateTime nowUtc) =>
        new(
            projectId,
            "INSUFFICIENT_DATA",
            "Chưa đủ tín hiệu vận hành (chưa có Gate, Task hoặc Báo cáo tuần) để kết luận sức khỏe đồ án.",
            new HealthPulseSignalsDto(null, 0, 0, 0, null),
            nowUtc);
}
