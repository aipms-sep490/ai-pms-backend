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
    public async Task<TaskBreakdownDto> BreakdownTaskAsync(long projectId, TaskBreakdownRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var (project, actor, actorName, scope) = await RequireProjectAccessAsync(projectId, ct);

        string taskTitle = (request.Title ?? string.Empty).Trim();
        string taskDescription = (request.Description ?? string.Empty).Trim();
        long scopeId = projectId;
        string scopeType = "PROJECT";

        var scopedTasks = ScopedTasksQuery(projectId, scope);

        if (request.TaskId is long taskId && taskId > 0)
        {
            var existingTask = await scopedTasks
                .SingleOrDefaultAsync(t => t.Id == taskId, ct)
                ?? throw new NotFoundException("Task", taskId);
            if (string.IsNullOrWhiteSpace(taskTitle)) taskTitle = existingTask.Title;
            if (string.IsNullOrWhiteSpace(taskDescription)) taskDescription = existingTask.Description ?? string.Empty;
            scopeId = taskId;
            scopeType = "TASK";
        }

        if (taskTitle.Length is < 3 or > 500)
            throw Invalid("Cần nhập tên công việc từ 3 đến 500 ký tự để AI chia nhỏ tiêu chí.");

        var membersQuery = db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == project.TeamId && m.LeftAt == null);
        if (!scope.IsFullProjectAccess)
        {
            var allowed = scope.AllowedMajorIds.ToArray();
            membersQuery = membersQuery.Where(m => m.User.MajorId.HasValue && allowed.Contains(m.User.MajorId.Value));
        }
        var members = await membersQuery
            .Select(m => new
            {
                m.UserId,
                m.User.FullName,
                MajorName = m.User.Major != null ? m.User.Major.Name : null,
                IsLeader = m.IsLeader
            })
            .ToListAsync(ct);

        var openCounts = await db.TaskAssignees.AsNoTracking()
            .Where(ta => ta.Task.DeletedAt == null && ta.Task.Milestone.ProjectId == projectId && ta.Task.Status != "DONE" && ta.Task.Status != "CANCELLED")
            .GroupBy(ta => ta.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var existingTasks = await scopedTasks
            .OrderByDescending(t => t.UpdatedAt)
            .Take(20)
            .Select(t => new { t.Id, t.Title, t.Status, t.Priority })
            .ToListAsync(ct);

        var memberCatalog = members.Select(m => new
        {
            m.UserId,
            m.FullName,
            m.MajorName,
            Role = m.IsLeader ? "Trưởng nhóm" : "Thành viên",
            OpenTaskCount = openCounts.GetValueOrDefault(m.UserId, 0)
        }).ToList();

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_TASK_BREAKDOWN] Chia nhỏ một Task đồ án thành danh sách 4–7 tiêu chí hoàn thành (TaskCriteria) cụ thể, đo lường được; ước lượng độ ưu tiên, Story Points (1,2,3,5,8,13), rủi ro và gợi ý 1–2 thành viên phù hợp nhất dựa trên chuyên ngành và số task đang mở. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"suggestedPriority\":\"LOW\"|\"MEDIUM\"|\"HIGH\"|\"CRITICAL\",\"suggestedStoryPoints\":number,\"implementationSummary\":string,\"criteria\":[{\"title\":string,\"verificationHint\":string}],\"potentialRisks\":string[],\"relatedExistingTasks\":string[],\"recommendedAssignees\":[{\"userId\":number,\"reason\":string}]}. " +
            "Chỉ chọn userId xuất hiện trong danh sách members.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            taskTitle,
            taskDescription = Clip(taskDescription, 1500),
            members = memberCatalog,
            existingTasks
        }, ct);

        var status = ResolveGenerationStatus(true);
        var priority = "MEDIUM";
        var storyPoints = 3;
        var implSummary = $"Gợi ý chia nhỏ tiêu chí thực hiện cho công việc \"{taskTitle}\".";
        var criteria = new List<TaskBreakdownCriterionDto>();
        var risks = Array.Empty<string>();
        var related = Array.Empty<string>();
        var recs = new List<TaskAssigneeRecommendationDto>();

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            var p = ReadString(r, "suggestedPriority", "MEDIUM", 16).ToUpperInvariant();
            if (p is "LOW" or "MEDIUM" or "HIGH" or "CRITICAL") priority = p;
            storyPoints = ReadInt(r, "suggestedStoryPoints", 3, 1, 21);
            implSummary = ReadString(r, "implementationSummary", implSummary, 1500);
            risks = ReadStringArray(r, "potentialRisks", 6, 260);
            related = ReadStringArray(r, "relatedExistingTasks", 6, 200);

            if (r.TryGetProperty("criteria", out var cArr) && cArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cArr.EnumerateArray().Take(10))
                {
                    var ctTitle = ReadString(c, "title", "", 480);
                    if (string.IsNullOrWhiteSpace(ctTitle)) continue;
                    criteria.Add(new TaskBreakdownCriterionDto(ctTitle, ReadString(c, "verificationHint", "", 300)));
                }
            }

            var memberById = memberCatalog.ToDictionary(m => m.UserId);
            if (r.TryGetProperty("recommendedAssignees", out var rArr) && rArr.ValueKind == JsonValueKind.Array)
            {
                var seen = new HashSet<long>();
                foreach (var ra in rArr.EnumerateArray())
                {
                    if (!ra.TryGetProperty("userId", out var uidProp) || !uidProp.TryGetInt64(out var uid)) continue;
                    if (!memberById.TryGetValue(uid, out var mem) || !seen.Add(uid)) continue;
                    recs.Add(new TaskAssigneeRecommendationDto(uid, mem.FullName, mem.MajorName, mem.OpenTaskCount, ReadString(ra, "reason", "Phù hợp năng lực và tải công việc.", 300)));
                    if (recs.Count >= 3) break;
                }
            }
        }
        else
        {
            status = "FALLBACK";
        }

        var snapPayload = new { taskTitle, priority, storyPoints, implSummary, criteria, risks, related, recs };
        var meta = await SaveSnapshotAsync("TASK_BREAKDOWN", scopeType, scopeId, projectId, null, status, $"Chia nhỏ Task: {Clip(taskTitle, 120)}", snapPayload, actor, actorName, ct);

        return new TaskBreakdownDto(projectId, taskTitle, priority, storyPoints, implSummary, criteria, risks, related, recs, meta);
    }

    public async Task<SprintRetroDto> GenerateSprintRetroAsync(long projectId, long sprintId, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var (project, actor, actorName, scope) = await RequireProjectAccessAsync(projectId, ct);

        var sprint = await db.Set<Sprint>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sprintId && s.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Sprint", sprintId);

        var tasks = await ScopedTasksQuery(projectId, scope)
            .Where(t => t.SprintId == sprintId)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Status,
                t.Priority,
                t.StoryPoints,
                t.DueAt,
                t.CompletedAt,
                Assignees = t.TaskAssignees.Select(a => a.User.FullName).ToArray()
            })
            .ToListAsync(ct);

        var completedCount = tasks.Count(t => t.Status == "DONE");
        var incompleteCount = tasks.Count - completedCount;

        var meetings = await db.MeetingAiInsights.AsNoTracking()
            .Where(i => i.Meeting.ProjectId == projectId && i.CreatedAt >= sprint.StartAt.AddDays(-2) && i.CreatedAt <= sprint.EndAt.AddDays(3))
            .OrderByDescending(i => i.CreatedAt)
            .Take(4)
            .Select(i => new { i.Meeting.Title, Summary = Clip(i.SummaryText, 350) })
            .ToListAsync(ct);

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_SPRINT_RETRO] Viết báo cáo Sprint Retrospective chuyên nghiệp dựa trên dữ liệu Task thực tế và biên bản cuộc họp trong Sprint. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"headline\":string,\"executiveSummary\":string,\"wentWell\":string[],\"needsImprovement\":string[],\"bottlenecks\":string[],\"nextSprintActions\":string[]}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            sprintName = sprint.Name,
            sprintStatus = sprint.Status,
            startAt = sprint.StartAt.ToString("yyyy-MM-dd"),
            endAt = sprint.EndAt.ToString("yyyy-MM-dd"),
            totalTasks = tasks.Count,
            completedTasks = completedCount,
            incompleteTasks = incompleteCount,
            tasks,
            meetingSummaries = meetings
        }, ct);

        var status = ResolveGenerationStatus(true);
        var headline = $"Sprint Retrospective — {sprint.Name}";
        var execSummary = $"Sprint \"{sprint.Name}\" hoàn thành {completedCount}/{tasks.Count} công việc.";
        string[] wentWell = [], needsImprovement = [], bottlenecks = [], nextActions = [];

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            headline = ReadString(r, "headline", headline, 240);
            execSummary = ReadString(r, "executiveSummary", execSummary, 1800);
            wentWell = ReadStringArray(r, "wentWell", 6, 300);
            needsImprovement = ReadStringArray(r, "needsImprovement", 6, 300);
            bottlenecks = ReadStringArray(r, "bottlenecks", 6, 300);
            nextActions = ReadStringArray(r, "nextSprintActions", 6, 300);
        }
        else
        {
            status = "FALLBACK";
        }

        var snapPayload = new { headline, execSummary, wentWell, needsImprovement, bottlenecks, nextActions };
        var meta = await SaveSnapshotAsync("SPRINT_RETRO", "SPRINT", sprintId, projectId, null, status, headline, snapPayload, actor, actorName, ct);
        return new SprintRetroDto(projectId, sprintId, sprint.Name, tasks.Count, completedCount, incompleteCount, headline, execSummary, wentWell, needsImprovement, bottlenecks, nextActions, meta);
    }
}
