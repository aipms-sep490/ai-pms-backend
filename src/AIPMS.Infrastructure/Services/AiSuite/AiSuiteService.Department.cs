using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiSuite;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.AiSuite;

internal sealed partial class AiSuiteService
{
    private const string FeatureDepartmentBrief = "DEPARTMENT_BRIEF";

    public async Task<DepartmentExecutiveBriefDto?> GetLatestDepartmentBriefAsync(long? departmentId, CancellationToken ct)
    {
        var actor = RequireActor();
        if (!IsStaffOrAdmin) throw new ForbiddenException("Chỉ Cán bộ Khoa hoặc Quản trị viên mới có quyền xem Bản tin điều hành.");
        var resolvedDeptId = await ResolveDepartmentScopeAsync(actor, departmentId, ct);
        var scopeId = resolvedDeptId ?? 0L;
        var snap = await LoadLatestSnapshotAsync(FeatureDepartmentBrief, "DEPARTMENT", scopeId, ct);
        if (snap is null) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<DepartmentExecutiveBriefDto>(snap.Value.Row.PayloadJson, JsonOptions);
            return dto is null ? null : dto with
            {
                Meta = new AiSnapshotMetaDto(snap.Value.Row.Id, FeatureDepartmentBrief, snap.Value.Row.Status, snap.Value.Row.ModelVersion, snap.Value.Row.CreatedAt, snap.Value.CreatorName)
            };
        }
        catch (JsonException) { return null; }
    }

    public async Task<DepartmentExecutiveBriefDto> GenerateDepartmentBriefAsync(DepartmentBriefRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var actor = RequireActor();
        if (!IsStaffOrAdmin) throw new ForbiddenException("Chỉ Cán bộ Khoa hoặc Quản trị viên mới có quyền tạo Bản tin điều hành Khoa.");

        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == actor).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? $"Cán bộ #{actor}";
        var resolvedDeptId = await ResolveDepartmentScopeAsync(actor, request.DepartmentId, ct);

        var scopeName = "Toàn hệ thống";
        if (resolvedDeptId is long deptId)
        {
            scopeName = await db.Departments.AsNoTracking()
                .Where(d => d.Id == deptId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(ct) ?? $"Khoa #{deptId}";
        }

        var projectsQuery = db.Projects.AsNoTracking().Where(p => p.Status == "ACTIVE");
        if (resolvedDeptId is long filterDeptId)
        {
            var deptTeamIds = db.TeamMembers.AsNoTracking()
                .Where(tm => tm.LeftAt == null && tm.User.DepartmentId == filterDeptId)
                .Select(tm => tm.TeamId);
            projectsQuery = projectsQuery.Where(p => deptTeamIds.Contains(p.TeamId));
        }

        var projects = await projectsQuery
            .OrderByDescending(p => p.UpdatedAt)
            .Take(60)
            .Select(p => new { p.Id, p.Code, p.Title, p.TeamId })
            .ToListAsync(ct);

        var projectIds = projects.Select(p => p.Id).ToArray();
        var now = Now;

        var taskStats = projectIds.Length == 0
            ? []
            : await db.Tasks.AsNoTracking()
                .Where(t => projectIds.Contains(t.Milestone.ProjectId) && t.Status != "DONE" && t.Status != "CANCELLED")
                .GroupBy(t => t.Milestone.ProjectId)
                .Select(g => new
                {
                    ProjectId = g.Key,
                    OverdueCount = g.Count(t => t.DueAt.HasValue && t.DueAt.Value < now),
                    BlockedCount = g.Count(t => t.Status == "BLOCKED")
                })
                .ToDictionaryAsync(x => x.ProjectId, ct);

        var lastMeetings = projectIds.Length == 0
            ? new Dictionary<long, DateTime>()
            : await db.Meetings.AsNoTracking()
                .Where(m => projectIds.Contains(m.ProjectId) && m.Status != "CANCELLED")
                .GroupBy(m => m.ProjectId)
                .Select(g => new { ProjectId = g.Key, LastAt = g.Max(m => m.StartAt) })
                .ToDictionaryAsync(x => x.ProjectId, x => x.LastAt, ct);

        var supervisorLoads = projectIds.Length == 0
            ? []
            : await db.SupervisorAssignments.AsNoTracking()
                .Where(a => projectIds.Contains(a.ProjectId) && a.EndedAt == null)
                .GroupBy(a => new { a.SupervisorProfile.UserId, a.SupervisorProfile.User.FullName })
                .Select(g => new { g.Key.UserId, g.Key.FullName, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(ct);

        var pendingAppealsCount = await db.Set<ResultAppealRequest>().AsNoTracking()
            .CountAsync(a => a.Status == "PENDING" && (!resolvedDeptId.HasValue || a.ResponsibleDepartmentId == resolvedDeptId.Value), ct);

        var riskCandidates = projects.Select(p =>
        {
            var ts = taskStats.GetValueOrDefault(p.Id);
            var overdue = ts?.OverdueCount ?? 0;
            var blocked = ts?.BlockedCount ?? 0;
            var daysSilent = lastMeetings.TryGetValue(p.Id, out var lastMtg)
                ? Math.Max(0, (int)Math.Floor((now - lastMtg).TotalDays))
                : 30;
            var riskScore = overdue * 3 + blocked * 4 + (daysSilent >= 14 ? 5 : 0);
            var level = riskScore >= 12 ? "CRITICAL" : riskScore >= 6 ? "HIGH" : riskScore >= 3 ? "MEDIUM" : "LOW";
            return new { p.Id, p.Code, p.Title, Overdue = overdue, Blocked = blocked, DaysSilent = daysSilent, RiskScore = riskScore, Level = level };
        }).ToList();

        var topRisks = riskCandidates
            .Where(x => x.RiskScore >= 3)
            .OrderByDescending(x => x.RiskScore)
            .Take(8)
            .ToList();

        var silentTeamCount = riskCandidates.Count(x => x.DaysSilent >= 14);
        var atRiskCount = riskCandidates.Count(x => x.Level is "CRITICAL" or "HIGH");

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_DEPARTMENT_BRIEF] Soạn Bản tin Điều hành Tuần (Executive Brief) ngắn gọn, sắc bén cho Chủ nhiệm Khoa / Giáo vụ từ số liệu vận hành thực tế. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"headline\":string,\"executiveSummary\":string,\"riskInsights\":[{\"projectId\":number,\"reason\":string,\"recommendedAction\":string}],\"supervisorNotes\":[{\"userId\":number,\"note\":string}],\"weeklyPriorityActions\":string[]}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            scopeName,
            currentTimeVietnam = VietnamNowIso,
            totalActiveProjects = projects.Count,
            atRiskProjectCount = atRiskCount,
            silentTeamCount,
            pendingAppealsCount,
            topRiskProjects = topRisks,
            supervisorLoads
        }, ct);

        var status = "COMPLETED";
        var headline = $"Bản tin điều hành tuần — {scopeName}";
        var execSummary = $"Đang theo dõi {projects.Count} đồ án hoạt động ({atRiskCount} đồ án rủi ro cao, {silentTeamCount} nhóm > 14 ngày chưa họp, {pendingAppealsCount} khiếu nại chờ xử lý).";
        var reasonMap = new Dictionary<long, (string Reason, string Action)>();
        var supNoteMap = new Dictionary<long, string>();
        string[] weeklyActions = [];

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            headline = ReadString(r, "headline", headline, 240);
            execSummary = ReadString(r, "executiveSummary", execSummary, 2000);
            weeklyActions = ReadStringArray(r, "weeklyPriorityActions", 6, 300);

            if (r.TryGetProperty("riskInsights", out var riArr) && riArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var ri in riArr.EnumerateArray())
                {
                    if (!ri.TryGetProperty("projectId", out var pidProp) || !pidProp.TryGetInt64(out var pid)) continue;
                    reasonMap[pid] = (ReadString(ri, "reason", "", 300), ReadString(ri, "recommendedAction", "", 300));
                }
            }
            if (r.TryGetProperty("supervisorNotes", out var snArr) && snArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var sn in snArr.EnumerateArray())
                {
                    if (!sn.TryGetProperty("userId", out var uidProp) || !uidProp.TryGetInt64(out var uid)) continue;
                    supNoteMap[uid] = ReadString(sn, "note", "", 300);
                }
            }
        }
        else
        {
            status = "FALLBACK";
        }

        var finalRisks = topRisks.Select(x =>
        {
            var found = reasonMap.TryGetValue(x.Id, out var pair);
            return new DepartmentRiskProjectDto(
                x.Id, x.Code, x.Title, x.Level, x.Overdue, x.Blocked, x.DaysSilent,
                found && !string.IsNullOrWhiteSpace(pair.Reason) ? pair.Reason : $"{x.Overdue} task quá hạn, {x.Blocked} task bị chặn, {x.DaysSilent} ngày chưa họp.",
                found && !string.IsNullOrWhiteSpace(pair.Action) ? pair.Action : "Nhắc Trưởng nhóm và GVHD rà soát lại mốc và mở khóa các task bị chặn.");
        }).ToArray();

        var finalSupAlerts = supervisorLoads
            .Where(s => s.Count >= 4 || supNoteMap.ContainsKey(s.UserId))
            .Select(s => new DepartmentSupervisorLoadDto(
                s.UserId, s.FullName, s.Count,
                supNoteMap.GetValueOrDefault(s.UserId, $"Đang hướng dẫn {s.Count} đồ án hoạt động.")))
            .ToArray();

        var preliminary = new DepartmentExecutiveBriefDto(
            resolvedDeptId, scopeName, projects.Count, atRiskCount, silentTeamCount, pendingAppealsCount,
            headline, execSummary, finalRisks, finalSupAlerts, weeklyActions,
            new AiSnapshotMetaDto(null, FeatureDepartmentBrief, status, aiProvider.ModelVersion, Now, actorName));

        var meta = await SaveSnapshotAsync(FeatureDepartmentBrief, "DEPARTMENT", resolvedDeptId ?? 0L, null, resolvedDeptId, status, headline, preliminary, actor, actorName, ct);
        return preliminary with { Meta = meta };
    }

    private async Task<long?> ResolveDepartmentScopeAsync(long actor, long? requestedDepartmentId, CancellationToken ct)
    {
        if (IsAdmin) return requestedDepartmentId;
        var staffDept = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor)
            .Select(u => u.DepartmentId)
            .FirstOrDefaultAsync(ct);
        if (requestedDepartmentId.HasValue && staffDept.HasValue && requestedDepartmentId.Value != staffDept.Value)
            throw new ForbiddenException("Bạn chỉ có thể xem bản tin điều hành thuộc Khoa mình phụ trách.");
        return staffDept ?? requestedDepartmentId;
    }
}
