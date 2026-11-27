using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiSuite;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.AiSuite;

internal sealed partial class AiSuiteService
{
    private const string FeatureFairness = "CONTRIBUTION_FAIRNESS";

    public async Task<ContributionFairnessDto?> GetLatestContributionFairnessAsync(long projectId, CancellationToken ct)
    {
        var (_, actor, _) = await RequireProjectAccessAsync(projectId, ct);
        var includePrivate = await IsSupervisorOrStaffForProjectAsync(projectId, actor, ct);
        var snap = await LoadLatestSnapshotAsync(FeatureFairness, "PROJECT", projectId, ct);
        if (snap is null) return null;

        try
        {
            var stored = JsonSerializer.Deserialize<ContributionFairnessPayload>(snap.Value.Row.PayloadJson, JsonOptions);
            if (stored is null) return null;
            var members = includePrivate
                ? stored.Members
                : stored.Members.Select(m => m with { AveragePeerRating = null }).ToArray();
            return new ContributionFairnessDto(
                projectId,
                stored.BalanceScore,
                stored.Headline,
                stored.ExecutiveSummary,
                includePrivate,
                members,
                stored.RebalanceRecommendations,
                includePrivate ? stored.SupervisorVerificationQuestions : [],
                BuildSnapshotMeta(snap.Value.Row.Id, FeatureFairness, snap.Value.Row.Status, snap.Value.Row.ModelVersion, snap.Value.Row.CreatedAt, snap.Value.CreatorName));
        }
        catch (JsonException) { return null; }
    }

    public async Task<ContributionFairnessDto> AnalyzeContributionFairnessAsync(long projectId, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var (project, actor, actorName) = await RequireProjectAccessAsync(projectId, ct);
        var includePrivate = await IsSupervisorOrStaffForProjectAsync(projectId, actor, ct);

        var teamMembers = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == project.TeamId && m.LeftAt == null)
            .Select(m => new { m.UserId, m.User.FullName, Role = m.IsLeader ? "Trưởng nhóm" : "Thành viên" })
            .ToListAsync(ct);

        var memberIds = teamMembers.Select(m => m.UserId).ToArray();

        var taskRows = await db.TaskAssignees.AsNoTracking()
            .Where(ta => ta.Task.Milestone.ProjectId == projectId && ta.Task.Status != "CANCELLED" && memberIds.Contains(ta.UserId))
            .Select(ta => new { ta.UserId, ta.Task.Status, ta.Task.DueAt })
            .ToListAsync(ct);

        var criteriaRows = await db.TaskCriteria.AsNoTracking()
            .Where(c => c.Task.Milestone.ProjectId == projectId && c.IsCompleted && c.CompletedBy.HasValue && memberIds.Contains(c.CompletedBy.Value))
            .GroupBy(c => c.CompletedBy!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var commentRows = await db.TaskComments.AsNoTracking()
            .Where(c => c.Task.Milestone.ProjectId == projectId && memberIds.Contains(c.AuthorId))
            .GroupBy(c => c.AuthorId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var attendanceRows = await db.MeetingParticipants.AsNoTracking()
            .Where(p => p.Meeting.ProjectId == projectId && p.AttendanceStatus == "PRESENT" && memberIds.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Integrate live speech participation from Video Call AI!
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

        var weeklyRows = await db.Set<AIPMS.Infrastructure.Persistence.Models.WeeklyAssessment>().AsNoTracking()
            .Where(w => w.ProjectId == projectId && w.Status == "SUBMITTED" && w.TotalScore.HasValue && memberIds.Contains(w.StudentId))
            .GroupBy(w => w.StudentId)
            .Select(g => new { UserId = g.Key, Avg = g.Average(x => x.TotalScore!.Value) })
            .ToDictionaryAsync(x => x.UserId, x => (decimal?)Math.Round(x.Avg, 2), ct);

        var peerRatings = new Dictionary<long, double?>();
        if (includePrivate)
        {
            var peerRows = await db.Set<AIPMS.Infrastructure.Persistence.Models.PeerSubmissionItem>().AsNoTracking()
                .Where(pi => db.Set<AIPMS.Infrastructure.Persistence.Models.PeerSubmission>()
                    .Any(ps => ps.Id == pi.SubmissionId && ps.Status == "SUBMITTED"
                        && db.Set<AIPMS.Infrastructure.Persistence.Models.PeerRound>().Any(pr => pr.Id == ps.RoundId && pr.ProjectId == projectId))
                    && memberIds.Contains(pi.TargetUserId))
                .GroupBy(pi => pi.TargetUserId)
                .Select(g => new { UserId = g.Key, Avg = g.Average(x => (double)x.Rating) })
                .ToListAsync(ct);
            foreach (var pr in peerRows) peerRatings[pr.UserId] = Math.Round(pr.Avg, 2);
        }

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
                includePrivate ? peerRatings.GetValueOrDefault(m.UserId) : null,
                "BALANCED",
                "",
                []);
        }).ToList();

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_FAIRNESS] Phân tích mức độ công bằng đóng góp của các thành viên nhóm đồ án dựa trên số liệu đa chiều (Task, TaskCriteria, Comments, Điểm danh, Phát biểu Video Call, Weekly Assessment, Peer Rating). " +
            "Phát hiện sớm dấu hiệu quá tải (OVERLOADED_BURNOUT), thiếu tương tác/free-rider (LOW_ENGAGEMENT_RISK), hoặc lệch giữa peer rating và khối lượng thực tế (PEER_MISMATCH). " +
            "Tuyệt đối không kết tội cảm tính — mọi nhận xét đều phải dựa trên con số thật. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"balanceScore\":number,\"headline\":string,\"executiveSummary\":string,\"memberAssessments\":[{\"userId\":number,\"signal\":\"BALANCED\"|\"OVERLOADED_BURNOUT\"|\"LOW_ENGAGEMENT_RISK\"|\"PEER_MISMATCH\",\"assessment\":string,\"evidenceBullets\":string[]}],\"rebalanceRecommendations\":string[],\"supervisorVerificationQuestions\":string[]}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            includesPrivatePeerSignals = includePrivate,
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
            supQs = ReadStringArray(r, "supervisorVerificationQuestions", 5, 300);

            if (r.TryGetProperty("memberAssessments", out var ma) && ma.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in ma.EnumerateArray())
                {
                    if (!item.TryGetProperty("userId", out var uidProp) || !uidProp.TryGetInt64(out var uid)) continue;
                    var sig = ReadString(item, "signal", "BALANCED", 32).ToUpperInvariant();
                    if (sig is not ("BALANCED" or "OVERLOADED_BURNOUT" or "LOW_ENGAGEMENT_RISK" or "PEER_MISMATCH")) sig = "BALANCED";
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

        var storedPayload = new ContributionFairnessPayload(balanceScore, headline, execSummary, enrichedMembers, recs, supQs);
        var meta = await SaveSnapshotAsync(FeatureFairness, "PROJECT", projectId, projectId, null, status, headline, storedPayload, actor, actorName, ct);

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
