using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AcademicKnowledge.Abstractions;
using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ProjectAiSetupService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IProjectExecutionGuard executionGuard,
    ISender sender,
    IAuditTrail audit,
    TimeProvider clock) : IAiSetupService
{
    public async Task<ApplyProjectSetupResultDto> ApplyProjectSetupAsync(long projectId, ApplyProjectSetupRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId) throw new UnauthorizedException();
        if (request is null || request.IdempotencyKey == Guid.Empty) throw new ArgumentException("Thiếu mã xác thực chống trùng (idempotencyKey).");
        var milestones = request.Milestones ?? [];
        var deliverables = request.Deliverables ?? [];
        var tasks = request.Tasks ?? [];
        if (milestones.Count == 0 && deliverables.Count == 0 && tasks.Count == 0)
            throw new ArgumentException("Cần chọn ít nhất một mốc, hạng mục tài liệu hoặc công việc để áp dụng.");
        if (milestones.Count > 8 || deliverables.Count > 10 || tasks.Count > 15)
            throw new ArgumentException("Giới hạn mỗi lần áp dụng: tối đa 8 mốc, 10 hạng mục tài liệu và 15 công việc.");

        var replay = await db.AiSetupApplications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActorId == actorId && x.IdempotencyKey == request.IdempotencyKey && x.ScopeType == "PROJECT" && x.ProjectId == projectId, ct);
        if (replay is not null)
        {
            var prev = JsonSerializer.Deserialize<ApplyProjectSetupResultDto>(replay.ResultJson);
            if (prev is not null) return prev with { Replay = true };
        }

        await executionGuard.MustBeActiveAsync(projectId, ct);
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.TeamId, p.Status })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);

        var isLeader = await db.TeamMembers.AsNoTracking()
            .AnyAsync(m => m.TeamId == project.TeamId && m.UserId == actorId && m.IsLeader && m.LeftAt == null, ct);
        var isSupervisor = await db.SupervisorAssignments.AsNoTracking()
            .AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == actorId, ct);
        if (!isLeader && !isSupervisor)
            throw new ForbiddenException("Chỉ trưởng nhóm hoặc giảng viên hướng dẫn được tự động thiết lập mốc, tiêu chí và công việc cho đồ án.");

        ValidateNoDependencyCycles(tasks);

        var now = clock.GetUtcNow().UtcDateTime;
        var warnings = new List<string>();
        var createdItems = new List<CreatedSetupItemRef>();
        var useTx = db.Database.IsRelational();
        await using var tx = useTx ? await db.Database.BeginTransactionAsync(ct) : null;

        try
        {
            var existingMilestones = await db.Milestones.AsNoTracking()
                .Where(m => m.ProjectId == projectId)
                .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
                .Select(m => new { m.Id, m.Title, m.SortOrder })
                .ToListAsync(ct);
            var nextSort = existingMilestones.Count == 0 ? 1 : existingMilestones.Max(m => m.SortOrder) + 1;

            var teamMembers = await db.TeamMembers.AsNoTracking()
                .Where(m => m.TeamId == project.TeamId && m.LeftAt == null)
                .Select(m => new { m.UserId, m.User.FullName })
                .ToListAsync(ct);

            var projectMajors = await db.ProjectMajors.AsNoTracking()
                .Where(pm => pm.ProjectId == projectId)
                .Select(pm => new { pm.MajorId, pm.Major.Name })
                .ToListAsync(ct);

            // 1. Milestones
            var milestoneMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var milestonesCreated = 0;
            foreach (var ms in milestones)
            {
                var key = ms.TempKey?.Trim() ?? "";
                var title = ms.Title?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(title) || title.Length > 255)
                    throw new ArgumentException("Tên mốc tiến độ không hợp lệ (1–255 ký tự).");

                if (ms.ExistingMilestoneId.HasValue)
                {
                    if (!existingMilestones.Any(e => e.Id == ms.ExistingMilestoneId.Value))
                        throw new ConflictException($"Mốc #{ms.ExistingMilestoneId.Value} không thuộc đồ án hiện tại.");
                    if (!string.IsNullOrEmpty(key)) milestoneMap[key] = ms.ExistingMilestoneId.Value;
                    continue;
                }

                var matchedExisting = existingMilestones.FirstOrDefault(e => Normalize(e.Title) == Normalize(title));
                if (matchedExisting is not null)
                {
                    if (!string.IsNullOrEmpty(key)) milestoneMap[key] = matchedExisting.Id;
                    warnings.Add($"Đã tái sử dụng mốc sẵn có \"{matchedExisting.Title}\" để tránh tạo trùng.");
                    continue;
                }

                DateOnly? start = ParseDay(ms.StartDate);
                DateOnly? due = ParseDay(ms.DueDate);
                if (start.HasValue && due.HasValue && due < start)
                    throw new ArgumentException($"Mốc \"{title}\" có hạn chót trước ngày bắt đầu.");

                var row = new M.Milestone
                {
                    ProjectId = projectId,
                    Title = title,
                    Description = ms.Description?.Trim(),
                    StartDate = start,
                    DueDate = due,
                    Status = "PLANNED",
                    SortOrder = nextSort++,
                    CreatedBy = actorId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    ConcurrencyToken = Guid.NewGuid()
                };
                db.Milestones.Add(row);
                await db.SaveChangesAsync(ct);
                existingMilestones.Add(new { row.Id, row.Title, row.SortOrder });
                if (!string.IsNullOrEmpty(key)) milestoneMap[key] = row.Id;
                createdItems.Add(new(key, row.Id, row.Title, "MILESTONE"));
                milestonesCreated++;
            }

            long? ResolveMilestoneId(string? milestoneRef)
            {
                if (!string.IsNullOrWhiteSpace(milestoneRef))
                {
                    var key = milestoneRef.Trim();
                    if (milestoneMap.TryGetValue(key, out var mapped)) return mapped;
                    if (long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
                        && existingMilestones.Any(e => e.Id == numeric))
                        return numeric;
                    var byTitle = existingMilestones.FirstOrDefault(e => Normalize(e.Title) == Normalize(key));
                    if (byTitle is not null) return byTitle.Id;
                }
                return existingMilestones.FirstOrDefault()?.Id;
            }

            // 2. Deliverables (Hạng mục tài liệu & tiêu chí đầu ra)
            var deliverablesCreated = 0;
            foreach (var del in deliverables)
            {
                var title = del.Title?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(title) || title.Length > 255)
                    throw new ArgumentException("Tên hạng mục tài liệu không hợp lệ (1–255 ký tự).");

                var msId = ResolveMilestoneId(del.MilestoneRef);
                var scope = string.Equals(del.EvidenceScope, "MAJOR_SPECIFIC", StringComparison.OrdinalIgnoreCase)
                    ? "MAJOR_SPECIFIC" : "COMMON";
                long? targetMajorId = null;
                if (scope == "MAJOR_SPECIFIC" && !string.IsNullOrWhiteSpace(del.TargetMajorName))
                {
                    var matchedMajors = projectMajors.Where(m => Normalize(m.Name) == Normalize(del.TargetMajorName)).ToArray();
                    if (matchedMajors.Length == 1) targetMajorId = matchedMajors[0].MajorId;
                    else
                    {
                        scope = "COMMON";
                        warnings.Add($"Hạng mục \"{title}\": không khớp duy nhất ngành \"{del.TargetMajorName}\", đã đặt phạm vi chung (COMMON).");
                    }
                }
                else if (scope == "MAJOR_SPECIFIC")
                {
                    scope = "COMMON";
                }

                var criteria = (del.AcceptanceCriteria ?? [])
                    .Select(c => c?.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)).Take(10).ToArray();
                var description = del.Description?.Trim() ?? "";
                if (criteria.Length > 0)
                {
                    var block = "Tiêu chí đạt yêu cầu:\n" + string.Join("\n", criteria.Select(c => $"- {c}"));
                    description = string.IsNullOrWhiteSpace(description) ? block : $"{description}\n\n{block}";
                }

                var row = new M.Deliverable
                {
                    ProjectId = projectId,
                    MilestoneId = msId,
                    Title = title,
                    Description = string.IsNullOrWhiteSpace(description) ? null : description,
                    DeliverableType = string.IsNullOrWhiteSpace(del.DeliverableType) ? "DOCUMENT" : del.DeliverableType.Trim().ToUpperInvariant(),
                    EvidenceScope = scope,
                    TargetMajorId = targetMajorId,
                    DueAt = del.DueAt?.UtcDateTime,
                    Status = "OPEN",
                    CreatedBy = actorId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Deliverables.Add(row);
                await db.SaveChangesAsync(ct);
                createdItems.Add(new(del.TempKey ?? "", row.Id, row.Title, "DELIVERABLE"));
                deliverablesCreated++;
            }

            // 3. Tasks + TaskCriteria + Assignees + Disciplines
            var taskMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var tasksCreated = 0;
            var taskCriteriaCreated = 0;
            foreach (var task in tasks)
            {
                var title = task.Title?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(title) || title.Length > 255)
                    throw new ArgumentException("Tên công việc không hợp lệ (1–255 ký tự).");

                var msId = ResolveMilestoneId(task.MilestoneRef)
                    ?? throw new ArgumentException("Cần có ít nhất một mốc tiến độ trong đồ án để tạo công việc.");
                var priority = task.Priority?.Trim().ToUpperInvariant();
                if (priority is not ("LOW" or "MEDIUM" or "HIGH" or "CRITICAL")) priority = "MEDIUM";

                var row = new M.Task
                {
                    MilestoneId = msId,
                    Title = title,
                    Description = string.IsNullOrWhiteSpace(task.Description) ? null : task.Description.Trim(),
                    Status = "TODO",
                    Priority = priority,
                    DueAt = task.DueAt?.UtcDateTime,
                    CreatedBy = actorId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    ConcurrencyToken = Guid.NewGuid()
                };
                db.Tasks.Add(row);
                await db.SaveChangesAsync(ct);

                var key = task.TempKey?.Trim() ?? "";
                if (!string.IsNullOrEmpty(key)) taskMap[key] = row.Id;
                createdItems.Add(new(key, row.Id, row.Title, "TASK"));
                tasksCreated++;

                // Assignees (unique exact name match among active team members)
                var assignedIds = new HashSet<long>();
                foreach (var name in (task.AssigneeNames ?? []).Where(n => !string.IsNullOrWhiteSpace(n)))
                {
                    var matched = teamMembers.Where(m => Normalize(m.FullName) == Normalize(name)).ToArray();
                    if (matched.Length == 1 && assignedIds.Add(matched[0].UserId))
                    {
                        db.TaskAssignees.Add(new M.TaskAssignee
                        {
                            TaskId = row.Id,
                            UserId = matched[0].UserId,
                            AssignedBy = actorId,
                            AssignedAt = now
                        });
                    }
                    else if (matched.Length != 1)
                    {
                        warnings.Add($"Công việc \"{title}\": chưa tự gán \"{name}\" do không khớp duy nhất thành viên nhóm.");
                    }
                }

                // Task Discipline
                if (!string.IsNullOrWhiteSpace(task.PrimaryMajorName))
                {
                    var matchedMajors = projectMajors.Where(m => Normalize(m.Name) == Normalize(task.PrimaryMajorName)).ToArray();
                    if (matchedMajors.Length == 1)
                    {
                        db.TaskDisciplines.Add(new M.TaskDiscipline
                        {
                            TaskId = row.Id,
                            MajorId = matchedMajors[0].MajorId,
                            Role = "PRIMARY",
                            CreatedBy = actorId,
                            CreatedAt = now
                        });
                    }
                }

                // Task Acceptance Criteria (dbo.task_criteria)
                var criteria = (task.AcceptanceCriteria ?? [])
                    .Select(c => c?.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)).Take(10).ToArray();
                var order = 0;
                foreach (var crit in criteria)
                {
                    db.TaskCriteria.Add(new TaskCriterion
                    {
                        TaskId = row.Id,
                        Title = crit!.Length <= 500 ? crit : crit[..500],
                        IsCompleted = false,
                        SortOrder = order++,
                        CreatedBy = actorId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    taskCriteriaCreated++;
                }

                db.TaskStatusHistories.Add(new M.TaskStatusHistory
                {
                    TaskId = row.Id,
                    OldStatus = null,
                    NewStatus = "TODO",
                    ChangedBy = actorId,
                    ChangedAt = now,
                    Reason = "Khởi tạo qua trợ lý thiết lập đồ án AI"
                });
            }
            await db.SaveChangesAsync(ct);

            // 4. Task Dependencies (FINISH_TO_START among newly created tasks)
            var dependenciesCreated = 0;
            var addedDeps = new HashSet<(long TaskId, long DependsOnId)>();
            foreach (var task in tasks)
            {
                var key = task.TempKey?.Trim() ?? "";
                if (string.IsNullOrEmpty(key) || !taskMap.TryGetValue(key, out var taskId)) continue;
                foreach (var depRef in (task.DependsOnRefs ?? []).Where(r => !string.IsNullOrWhiteSpace(r)))
                {
                    if (!taskMap.TryGetValue(depRef.Trim(), out var dependsOnId) || dependsOnId == taskId) continue;
                    if (!addedDeps.Add((taskId, dependsOnId))) continue;
                    db.TaskDependencies.Add(new M.TaskDependency
                    {
                        TaskId = taskId,
                        DependsOnTaskId = dependsOnId,
                        DependencyType = "FINISH_TO_START",
                        CreatedAt = now
                    });
                    dependenciesCreated++;
                }
            }
            await db.SaveChangesAsync(ct);

            var result = new ApplyProjectSetupResultDto(projectId, false, milestonesCreated, deliverablesCreated,
                tasksCreated, taskCriteriaCreated, dependenciesCreated, createdItems, warnings);

            db.AiSetupApplications.Add(new AiSetupApplication
            {
                ActorId = actorId,
                IdempotencyKey = request.IdempotencyKey,
                ScopeType = "PROJECT",
                ProjectId = projectId,
                RunId = request.RunId,
                ResultJson = JsonSerializer.Serialize(result),
                CreatedAt = now
            });
            await db.SaveChangesAsync(ct);

            await audit.RecordAsync(new AuditEntry(actorId, "AI_PROJECT_SETUP_APPLIED", "PROJECT", projectId,
                new Dictionary<string, object?>
                {
                    ["runId"] = request.RunId,
                    ["milestones"] = milestonesCreated,
                    ["deliverables"] = deliverablesCreated,
                    ["tasks"] = tasksCreated,
                    ["taskCriteria"] = taskCriteriaCreated,
                    ["dependencies"] = dependenciesCreated
                }), ct);

            if (tx is not null) await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ApplyRubricSetupResultDto> ApplyRubricSetupAsync(ApplyRubricSetupRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId) throw new UnauthorizedException();
        if (request is null || request.IdempotencyKey == Guid.Empty) throw new ArgumentException("Thiếu mã xác thực chống trùng (idempotencyKey).");
        if (request.Criteria is null || request.Criteria.Count == 0)
            throw new ArgumentException("Bộ tiêu chí cần ít nhất một tiêu chí.");

        var replay = await db.AiSetupApplications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActorId == actorId && x.IdempotencyKey == request.IdempotencyKey && x.ScopeType == "RUBRIC", ct);
        if (replay is not null)
        {
            var prev = JsonSerializer.Deserialize<ApplyRubricSetupResultDto>(replay.ResultJson);
            if (prev is not null) return prev with { Replay = true };
        }

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId && u.Status == "ACTIVE")
            .Select(u => new { u.Id, u.DepartmentId, OrganizationId = u.Department == null ? (long?)null : u.Department.OrganizationId })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException("Tài khoản không hợp lệ.");

        var departmentId = request.DepartmentId ?? user.DepartmentId
            ?? throw new ArgumentException("Cần chỉ định khoa áp dụng cho bộ tiêu chí.");

        var semesterId = request.AcademicSemesterId;
        if (semesterId <= 0)
        {
            var orgId = user.OrganizationId ?? await db.Departments.Where(d => d.Id == departmentId).Select(d => (long?)d.OrganizationId).SingleOrDefaultAsync(ct);
            semesterId = await db.AcademicSemesters.AsNoTracking()
                .Where(s => s.OrganizationId == orgId && s.Status == "ACTIVE")
                .OrderByDescending(s => s.StartDate).ThenByDescending(s => s.Id)
                .Select(s => s.Id).FirstOrDefaultAsync(ct);
            if (semesterId <= 0) throw new ArgumentException("Không tìm thấy học kỳ đang hoạt động để tạo bộ tiêu chí.");
        }

        var criteriaInputs = BuildRubricHierarchy(request.Criteria);
        var scope = request.EvaluationScope?.Trim().ToUpperInvariant();
        if (scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) scope = "COMMON";
        long? majorId = scope == "COMMON" ? null : request.MajorId;
        var mode = request.ProjectMode?.Trim().ToUpperInvariant();
        if (mode is not ("SINGLE_MAJOR" or "INTERDISCIPLINARY")) mode = "SINGLE_MAJOR";
        var stage = request.EvaluationStage?.Trim().ToUpperInvariant();
        if (stage is not ("ON_GOING" or "COLD" or "DEFENSE")) stage = "DEFENSE";

        var createReq = new CreateRubricRequest(departmentId, semesterId, request.Code.Trim(), request.Name.Trim(), request.Description?.Trim(), criteriaInputs)
        {
            MajorId = majorId,
            ProjectMode = mode,
            EvaluationStage = stage,
            EvaluationScope = scope
        };
        var created = await sender.Send(new CreateRubricCommand(createReq), ct);
        var result = new ApplyRubricSetupResultDto(created.Id, created.Code, created.Name, created.Status, request.Criteria.Count, false);

        db.AiSetupApplications.Add(new AiSetupApplication
        {
            ActorId = actorId,
            IdempotencyKey = request.IdempotencyKey,
            ScopeType = "RUBRIC",
            ProjectId = null,
            RunId = request.RunId,
            ResultJson = JsonSerializer.Serialize(result),
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(actorId, "AI_RUBRIC_SETUP_APPLIED", "RUBRIC", created.Id,
            new Dictionary<string, object?> { ["runId"] = request.RunId, ["code"] = created.Code, ["criteriaCount"] = request.Criteria.Count }), ct);
        return result;
    }

    private static IReadOnlyList<RubricCriterionInput> BuildRubricHierarchy(IReadOnlyList<AssistantRubricCriterionDraftDto> flat)
    {
        var byKey = flat.Where(c => !string.IsNullOrWhiteSpace(c.TempKey))
            .GroupBy(c => c.TempKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var childrenByParent = flat.Where(c => !string.IsNullOrWhiteSpace(c.ParentTempKey) && byKey.ContainsKey(c.ParentTempKey!.Trim()))
            .ToLookup(c => c.ParentTempKey!.Trim(), StringComparer.OrdinalIgnoreCase);

        var roots = flat.Where(c => string.IsNullOrWhiteSpace(c.ParentTempKey) || !byKey.ContainsKey(c.ParentTempKey!.Trim())).ToArray();

        RubricCriterionInput MapNode(AssistantRubricCriterionDraftDto node, int sortOrder, HashSet<string> visited)
        {
            var key = node.TempKey?.Trim() ?? "";
            var kids = string.IsNullOrEmpty(key) || !visited.Add(key)
                ? []
                : childrenByParent[key].Select((child, idx) => MapNode(child, idx + 1, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase))).ToArray();
            var levels = (node.Levels ?? [])
                .OrderBy(l => l.LevelOrder)
                .Select((l, idx) => new RubricCriterionLevelInput(l.LevelOrder > 0 ? l.LevelOrder : idx + 1, l.Label.Trim(), l.Descriptor.Trim(), l.ScoreFrom, l.ScoreTo))
                .ToArray();
            return new RubricCriterionInput(node.Name.Trim(), node.Description?.Trim(), node.WeightPercent,
                node.MaxScore ?? 10m, sortOrder, node.IsRequired)
            {
                LearningOutcome = node.LearningOutcome?.Trim(),
                EvidenceGuidance = node.EvidenceGuidance?.Trim(),
                Levels = levels,
                Children = kids
            };
        }

        return roots.Select((root, idx) => MapNode(root, idx + 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase))).ToArray();
    }

    private static void ValidateNoDependencyCycles(IReadOnlyList<AssistantSetupTaskDto> tasks)
    {
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tasks)
        {
            var key = t.TempKey?.Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;
            graph[key] = (t.DependsOnRefs ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
        }
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0=unvisited, 1=visiting, 2=done
        bool HasCycle(string node)
        {
            if (state.TryGetValue(node, out var s)) return s == 1;
            state[node] = 1;
            if (graph.TryGetValue(node, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (graph.ContainsKey(dep) && HasCycle(dep)) return true;
                }
            }
            state[node] = 2;
            return false;
        }
        foreach (var key in graph.Keys)
        {
            if (HasCycle(key)) throw new ArgumentException("Danh sách công việc đề xuất có quan hệ phụ thuộc vòng tròn. Hãy kiểm tra lại thứ tự phụ thuộc.");
        }
    }

    private static string Normalize(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value.Normalize(System.Text.NormalizationForm.FormC).Trim(), @"\s+", " ").ToLowerInvariant();

    private static DateOnly? ParseDay(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
