using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiSuite;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.AiSuite;

internal sealed partial class AiSuiteService
{
    private const string FeatureFairnessStaff = "CONTRIBUTION_FAIRNESS_STAFF";
    private const string FeatureFairnessStudent = "CONTRIBUTION_FAIRNESS_STUDENT";

    private static string ResolveFairnessCacheKey(ProjectActorScope scope, bool includePrivate)
    {
        if (scope.IsMentor)
        {
            var suffix = string.Join("_", scope.AllowedMajorIds.OrderBy(id => id));
            return $"CONTRIBUTION_FAIRNESS_MENTOR_{suffix}";
        }
        return includePrivate ? FeatureFairnessStaff : FeatureFairnessStudent;
    }

    public async Task<ContributionFairnessDto?> GetLatestContributionFairnessAsync(long projectId, CancellationToken ct)
    {
        var (_, actor, _, scope) = await RequireProjectAccessAsync(projectId, ct);
        var includePrivate = !scope.IsMentor && await IsSupervisorOrStaffForProjectAsync(projectId, actor, ct);
        var featureKey = ResolveFairnessCacheKey(scope, includePrivate);
        var snap = await LoadLatestSnapshotAsync(featureKey, "PROJECT", projectId, ct);
        if (snap is null) return null;

        try
        {
            var stored = JsonSerializer.Deserialize<ContributionFairnessPayload>(snap.Value.Row.PayloadJson, JsonOptions);
            if (stored is null) return null;
            var members = stored.Members.Select(m => m with { AveragePeerRating = null }).ToArray();
            return new ContributionFairnessDto(
                projectId,
                stored.BalanceScore,
                stored.Headline,
                stored.ExecutiveSummary,
                includePrivate,
                members,
                stored.RebalanceRecommendations,
                includePrivate ? stored.SupervisorVerificationQuestions : [],
                BuildSnapshotMeta(snap.Value.Row.Id, featureKey, snap.Value.Row.Status, snap.Value.Row.ModelVersion, snap.Value.Row.CreatedAt, snap.Value.CreatorName));
        }
        catch (JsonException) { return null; }
    }

    public async Task<ContributionFairnessDto> AnalyzeContributionFairnessAsync(long projectId, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var (project, actor, actorName, scope) = await RequireProjectAccessAsync(projectId, ct);
        var includePrivate = !scope.IsMentor && await IsSupervisorOrStaffForProjectAsync(projectId, actor, ct);
        var featureKey = ResolveFairnessCacheKey(scope, includePrivate);

        var membersQuery = db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == project.TeamId && m.LeftAt == null);
        if (scope.IsMentor)
        {
            var allowed = scope.AllowedMajorIds.ToArray();
            membersQuery = membersQuery.Where(m => m.User.MajorId.HasValue && allowed.Contains(m.User.MajorId.Value));
        }

        var teamMembers = await membersQuery
            .Select(m => new { m.UserId, m.User.FullName, Role = m.IsLeader ? "Trưởng nhóm" : "Thành viên" })
            .ToListAsync(ct);

        var memberIds = teamMembers.Select(m => m.UserId).ToArray();
        var scopedTaskIds = ScopedTasksQuery(projectId, scope).Select(t => t.Id);

        var taskRows = await db.TaskAssignees.AsNoTracking()
            .Where(ta => scopedTaskIds.Contains(ta.TaskId) && memberIds.Contains(ta.UserId))
            .Select(ta => new { ta.UserId, ta.Task.Status, ta.Task.DueAt })
            .ToListAsync(ct);

        var criteriaRows = await db.TaskCriteria.AsNoTracking()
            .Where(c => scopedTaskIds.Contains(c.TaskId) && c.IsCompleted && c.CompletedBy.HasValue && memberIds.Contains(c.CompletedBy.Value))
            .GroupBy(c => c.CompletedBy!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var commentRows = await db.TaskComments.AsNoTracking()
            .Where(c => scopedTaskIds.Contains(c.TaskId) && memberIds.Contains(c.AuthorId))
            .GroupBy(c => c.AuthorId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var attendanceRows = await db.MeetingParticipants.AsNoTracking()
            .Where(p => p.Meeting.ProjectId == projectId && p.AttendanceStatus == "PRESENT" && memberIds.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        Dictionary<long, int> speechCounts = new();
        try
        {
            speechCounts = await db.MeetingTranscriptSegments.AsNoTracking()
                .Where(s => s.Meeting.ProjectId == projectId && s.SpeakerUserId.HasValue && memberIds.Contains(s.SpeakerUserId.Value))
                .GroupBy(s => s.SpeakerUserId!.Value)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        }
        catch
        {
            // Keep resilient if transcript table is empty/unavailable.
        }

        // WeeklyAssessment is reference-only (0% weight in V4.0)
        var weeklyRows = await db.Set<WeeklyAssessment>().AsNoTracking()
            .Where(w => w.ProjectId == projectId && w.Status == "SUBMITTED" && w.TotalScore.HasValue && memberIds.Contains(w.StudentId))
            .GroupBy(w => w.StudentId)
            .Select(g => new { UserId = g.Key, Avg = g.Average(x => x.TotalScore!.Value) })
            .ToDictionaryAsync(x => x.UserId, x => (decimal?)Math.Round(x.Avg, 2), ct);

        // V4.0 POL-03 official 20% process scores (only visible to Primary Supervisor / Department Staff)
        var officialGateProcessScores = includePrivate
            ? await db.Set<GateIndividualProcessScore>().AsNoTracking()
                .Where(g => g.ProjectId == projectId && g.Score.HasValue && memberIds.Contains(g.StudentId))
                .GroupBy(g => g.StudentId)
                .Select(g => new { UserId = g.Key, Avg = g.Average(x => x.Score!.Value), FinalizedCount = g.Count(x => x.Status == "FINALIZED") })
                .ToListAsync(ct)
            : [];

        var totalSpeech = Math.Max(1, speechCounts.Values.Sum());
        var now = Now;

        var rawMembers = teamMembers.Select(m =>
        {
            var myTasks = taskRows.Where(t => t.UserId == m.UserId).ToList();
            var assigned = myTasks.Count;
            var done = myTasks.Count(t => t.Status == "DONE");
            var overdue = myTasks.Count(t => t.Status != "DONE" && t.DueAt.HasValue && t.DueAt.Value < now);
            var speech = speechCounts.GetValueOrDefault(m.UserId, 0);
            var speechShare = speechCounts.Values.Sum() == 0 ? 0.0 : Math.Round(speech * 100.0 / totalSpeech, 1);
            return new MemberFairnessMetricDto(
                m.UserId,
                m.FullName,
                m.Role,
                assigned,
                done,
                overdue,
                criteriaRows.GetValueOrDefault(m.UserId, 0),
                commentRows.GetValueOrDefault(m.UserId, 0),
                attendanceRows.GetValueOrDefault(m.UserId, 0),
                speech,
                speechShare,
                weeklyRows.GetValueOrDefault(m.UserId),
                null, // Peer Evaluation is disabled in V4.0 (G10)
                "BALANCED",
                "",
                []);
        }).ToList();

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_FAIRNESS] Phân tích mức độ công bằng đóng góp của các thành viên nhóm đồ án dựa trên số liệu đa chiều (Task, TaskCriteria, Comments, Điểm danh, Phát biểu Video Call, nhận xét tuần tham khảo 0%, và điểm quá trình 3 Gate POL-03 nếu có). " +
            "Lưu ý: Đánh giá đồng đẳng (Peer Evaluation) KHÔNG áp dụng trong V4.0 — KHÔNG đề cập Peer Rating hay PEER_MISMATCH. " +
            "Phát hiện sớm dấu hiệu quá tải (OVERLOADED_BURNOUT) hoặc thiếu tương tác/free-rider (LOW_ENGAGEMENT_RISK). " +
            "Tuyệt đối không kết tội cảm tính — mọi nhận xét đều phải dựa trên con số thật. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"balanceScore\":number,\"headline\":string,\"executiveSummary\":string,\"memberAssessments\":[{\"userId\":number,\"signal\":\"BALANCED\"|\"OVERLOADED_BURNOUT\"|\"LOW_ENGAGEMENT_RISK\",\"assessment\":string,\"evidenceBullets\":string[]}],\"rebalanceRecommendations\":string[],\"supervisorVerificationQuestions\":string[]}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            includesPrivateSignals = includePrivate,
            weeklyAssessmentReferenceOnly = true,
            officialGateProcessScores = includePrivate ? officialGateProcessScores : null,
            members = rawMembers
        }, ct);

        var status = ResolveGenerationStatus(true);
        int balanceScore = 75;
        string headline = $"Phân tích công bằng đóng góp — {project.Title}";
        string execSummary = "Tổng hợp dữ liệu đóng góp đa chiều theo từng thành viên.";
        string[] recs = [];
        string[] supQs = [];
        var assessmentMap = new Dictionary<long, (string Signal, string Assessment, string[] Bullets)>();

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            balanceScore = ReadInt(r, "balanceScore", 75, 0, 100);
            headline = ReadString(r, "headline", headline, 240);
            execSummary = ReadString(r, "executiveSummary", execSummary, 2000);
            recs = ReadStringArray(r, "rebalanceRecommendations", 6, 300);
            supQs = includePrivate ? ReadStringArray(r, "supervisorVerificationQuestions", 5, 300) : [];

            if (r.TryGetProperty("memberAssessments", out var ma) && ma.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in ma.EnumerateArray())
                {
                    if (!item.TryGetProperty("userId", out var uidProp) || !uidProp.TryGetInt64(out var uid)) continue;
                    var sig = ReadString(item, "signal", "BALANCED", 32).ToUpperInvariant();
                    if (sig is not ("BALANCED" or "OVERLOADED_BURNOUT" or "LOW_ENGAGEMENT_RISK")) sig = "BALANCED";
                    assessmentMap[uid] = (sig, ReadString(item, "assessment", "", 600), ReadStringArray(item, "evidenceBullets", 4, 200));
                }
            }
        }
        else
        {
            status = "FALLBACK";
        }

        var enrichedMembers = rawMembers.Select(m =>
        {
            if (assessmentMap.TryGetValue(m.UserId, out var a))
                return m with { Signal = a.Signal, Assessment = a.Assessment, EvidenceBullets = a.Bullets };
            var fallbackSignal = m.TasksOverdue >= 2 ? "OVERLOADED_BURNOUT" : m.TasksAssigned == 0 ? "LOW_ENGAGEMENT_RISK" : "BALANCED";
            return m with
            {
                Signal = fallbackSignal,
                Assessment = $"Hoàn thành {m.TasksCompleted}/{m.TasksAssigned} task, {m.CriteriaCompleted} tiêu chí, {m.SpeechSegments} lượt phát biểu video.",
                EvidenceBullets = [$"Task: {m.TasksCompleted}/{m.TasksAssigned} hoàn thành ({m.TasksOverdue} quá hạn)"]
            };
        }).ToArray();

        var storedPayload = new ContributionFairnessPayload(balanceScore, headline, execSummary, enrichedMembers, recs, includePrivate ? supQs : []);
        var meta = await SaveSnapshotAsync(featureKey, "PROJECT", projectId, projectId, null, status, headline, storedPayload, actor, actorName, ct);

        return new ContributionFairnessDto(
            projectId,
            balanceScore,
            headline,
            execSummary,
            includePrivate,
            enrichedMembers,
            recs,
            includePrivate ? supQs : [],
            meta);
    }

    private sealed record ContributionFairnessPayload(
        int BalanceScore,
        string Headline,
        string ExecutiveSummary,
        IReadOnlyList<MemberFairnessMetricDto> Members,
        IReadOnlyList<string> RebalanceRecommendations,
        IReadOnlyList<string> SupervisorVerificationQuestions);
}
