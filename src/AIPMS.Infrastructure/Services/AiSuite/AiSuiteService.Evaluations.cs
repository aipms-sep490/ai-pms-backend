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
    public async Task<RubricGradingCopilotDto> GenerateRubricGradingCopilotAsync(long assignmentId, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var actor = RequireActor();

        var assignment = await db.Set<EvaluationAssignment>().AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new NotFoundException("EvaluationAssignment", assignmentId);

        if (!IsAdmin && assignment.EvaluatorId != actor)
            throw new ForbiddenException("Chỉ giám khảo được phân công chấm mới có quyền dùng AI Copilot cho phiếu chấm này.");

        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == actor).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? $"Giám khảo #{actor}";
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == assignment.ProjectId, ct)
            ?? throw new NotFoundException("Project", assignment.ProjectId);

        var rubric = await db.Rubrics.AsNoTracking()
            .Include(r => r.RubricCriteria).ThenInclude(rc => rc.Criterion)
            .Include(r => r.RubricCriteria).ThenInclude(rc => rc.Levels)
            .SingleOrDefaultAsync(r => r.Id == assignment.RubricId, ct)
            ?? throw new NotFoundException("Rubric", assignment.RubricId);

        var leafCriteria = rubric.RubricCriteria
            .Where(rc => !rubric.RubricCriteria.Any(child => child.ParentId == rc.Id))
            .OrderBy(rc => rc.SortOrder)
            .ThenBy(rc => rc.Id)
            .ToList();

        var deliverables = await db.Deliverables.AsNoTracking()
            .Where(d => d.Milestone != null && d.Milestone.ProjectId == project.Id)
            .OrderByDescending(d => d.UpdatedAt)
            .Take(12)
            .Select(d => new { d.Title, d.Status, Description = Clip(d.Description, 250) })
            .ToListAsync(ct);

        var finalSub = await db.Set<FinalSubmission>().AsNoTracking()
            .Include(f => f.Items)
            .Where(f => f.ProjectId == project.Id)
            .OrderByDescending(f => f.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        var reports = await db.ProgressReports.AsNoTracking()
            .Where(r => r.ProjectId == project.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(4)
            .Select(r => new { r.ReportType, Summary = Clip(r.Summary, 350), Issues = Clip(r.IssuesAndRisks, 250) })
            .ToListAsync(ct);

        var taskSummary = await db.Tasks.AsNoTracking()
            .Where(t => t.Milestone.ProjectId == project.Id && t.Status != "CANCELLED")
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var meetingSummaries = await db.MeetingAiInsights.AsNoTracking()
            .Where(i => i.Meeting.ProjectId == project.Id)
            .OrderByDescending(i => i.CreatedAt)
            .Take(3)
            .Select(i => Clip(i.SummaryText, 300))
            .ToListAsync(ct);

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_RUBRIC_COPILOT] Hỗ trợ Giám khảo chấm đồ án theo Rubric bằng cách đối chiếu từng tiêu chí với bằng chứng thực tế của đồ án. " +
            "AI CHỈ đưa ra mức tham khảo, khoảng điểm gợi ý và nhận xét nháp có dẫn chứng — Giám khảo là người quyết định điểm chính thức. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"overallSummary\":string,\"criteria\":[{\"rubricCriterionId\":number,\"suggestedLevel\":string,\"suggestedScoreMin\":number|null,\"suggestedScoreMax\":number|null,\"suggestedComment\":string,\"evidenceCitations\":string[],\"confidence\":\"HIGH\"|\"MEDIUM\"|\"LOW\"}],\"verificationQuestions\":string[]}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            projectDescription = Clip(project.Description, 1000),
            rubricName = rubric.Name,
            evaluationStage = assignment.Stage,
            criteria = leafCriteria.Select(c => new
            {
                rubricCriterionId = c.Id,
                name = c.Criterion?.Name ?? $"Tiêu chí #{c.Id}",
                description = c.Criterion?.Description,
                weightPercent = c.WeightPercent,
                maxScore = c.MaxScore ?? 10m,
                evidenceGuidance = c.EvidenceGuidance,
                levels = c.Levels.OrderBy(l => l.LevelOrder).Select(l => new { l.Label, l.ScoreFrom, l.ScoreTo, l.Descriptor }).ToArray()
            }).ToArray(),
            deliverables,
            finalSubmissionItems = finalSub?.Items.Select(i => new { i.Title, i.VersionNumber, i.StatusAtSubmission }).ToArray() ?? [],
            reports,
            taskSummary,
            meetingSummaries
        }, ct);

        var status = ResolveGenerationStatus(true);
        var overallSummary = $"Đã đối chiếu hồ sơ đồ án {project.Code} với {leafCriteria.Count} tiêu chí của Rubric \"{rubric.Name}\".";
        var verificationQuestions = Array.Empty<string>();
        var suggestionMap = new Dictionary<long, RubricCriterionSuggestionDto>();

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            overallSummary = ReadString(r, "overallSummary", overallSummary, 2000);
            verificationQuestions = ReadStringArray(r, "verificationQuestions", 6, 300);

            if (r.TryGetProperty("criteria", out var cArr) && cArr.ValueKind == JsonValueKind.Array)
            {
                var byId = leafCriteria.ToDictionary(c => c.Id);
                foreach (var item in cArr.EnumerateArray())
                {
                    if (!item.TryGetProperty("rubricCriterionId", out var idProp) || !idProp.TryGetInt64(out var cid)) continue;
                    if (!byId.TryGetValue(cid, out var rc)) continue;
                    var max = rc.MaxScore ?? 10m;
                    var minScore = ReadDecimal(item, "suggestedScoreMin", 0m, max);
                    var maxScore = ReadDecimal(item, "suggestedScoreMax", 0m, max);
                    if (minScore.HasValue && maxScore.HasValue && minScore > maxScore) (minScore, maxScore) = (maxScore, minScore);
                    var conf = ReadString(item, "confidence", "MEDIUM", 12).ToUpperInvariant();
                    if (conf is not ("HIGH" or "MEDIUM" or "LOW")) conf = "MEDIUM";
                    suggestionMap[cid] = new RubricCriterionSuggestionDto(
                        cid,
                        rc.Criterion?.Name ?? $"Tiêu chí #{cid}",
                        rc.WeightPercent,
                        max,
                        ReadString(item, "suggestedLevel", "Tham khảo", 80),
                        minScore,
                        maxScore,
                        ReadString(item, "suggestedComment", "", 1000),
                        ReadStringArray(item, "evidenceCitations", 4, 240),
                        conf);
                }
            }
        }
        else
        {
            status = "FALLBACK";
        }

        var orderedSuggestions = leafCriteria.Select(rc =>
            suggestionMap.TryGetValue(rc.Id, out var found)
                ? found
                : new RubricCriterionSuggestionDto(
                    rc.Id,
                    rc.Criterion?.Name ?? $"Tiêu chí #{rc.Id}",
                    rc.WeightPercent,
                    rc.MaxScore ?? 10m,
                    "Cần giám khảo đánh giá trực tiếp",
                    null,
                    null,
                    "Chưa đủ bằng chứng tự động; giám khảo vui lòng kiểm chứng trực tiếp trên sản phẩm và phiên bảo vệ.",
                    [],
                    "LOW")).ToArray();

        const string disclaimer = "Gợi ý AI chỉ mang tính tham khảo hỗ trợ rà soát bằng chứng. Giám khảo chịu trách nhiệm quyết định điểm số và nhận xét chính thức.";
        var snapPayload = new { overallSummary, criteria = orderedSuggestions, verificationQuestions };
        var meta = await SaveSnapshotAsync("RUBRIC_GRADING", "EVALUATION", assignmentId, project.Id, assignment.DepartmentId, status, $"Copilot Rubric · {project.Code}", snapPayload, actor, actorName, ct);

        return new RubricGradingCopilotDto(
            assignmentId, project.Id, rubric.Name, overallSummary, orderedSuggestions, verificationQuestions, disclaimer, meta);
    }

    public async Task<ScoreCalibrationDto> CheckScoreCalibrationAsync(long projectId, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var actor = RequireActor();
        if (!IsStaffOrAdmin)
            throw new ForbiddenException("Chỉ Cán bộ Khoa hoặc Quản trị viên mới có quyền kiểm tra độ nhất quán điểm hội đồng.");

        var actorRecord = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new { u.FullName, u.DepartmentId })
            .FirstOrDefaultAsync(ct)
            ?? throw new ForbiddenException("Tài khoản không hoạt động.");
        var actorName = actorRecord.FullName ?? $"Cán bộ #{actor}";
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        if (!IsAdmin)
        {
            var scope = await AIPMS.Infrastructure.Services.Projects.ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: false);
            var inScope = actorRecord.DepartmentId.HasValue
                && (actorRecord.DepartmentId == scope.LeadDepartmentId || scope.DepartmentIds.Contains(actorRecord.DepartmentId.Value));
            if (!inScope)
                throw new ForbiddenException(
                    "Cán bộ Khoa chỉ được kiểm tra độ nhất quán điểm hội đồng cho đồ án thuộc phạm vi Khoa mình.",
                    WorkflowErrorCodes.ScopeAccessDenied);
        }

        var evals = await db.Evaluations.AsNoTracking()
            .Include(e => e.Evaluator)
            .Include(e => e.EvaluationDetails).ThenInclude(d => d.RubricCriterion).ThenInclude(rc => rc.Criterion)
            .Where(e => e.ProjectId == projectId)
            .ToListAsync(ct);

        // Deterministic variance calculation first (guaranteed accurate even before LLM!)
        var deterministicAlerts = new List<ScoreCalibrationAlertDto>();
        var byCriterion = evals
            .SelectMany(e => e.EvaluationDetails.Select(d => new
            {
                Evaluator = e.Evaluator.FullName,
                CriterionName = d.RubricCriterion.Criterion?.Name ?? $"Tiêu chí #{d.RubricCriterionId}",
                MaxScore = d.RubricCriterion.MaxScore ?? 10m,
                d.Score,
                d.Comments
            }))
            .GroupBy(x => x.CriterionName);

        foreach (var group in byCriterion)
        {
            var items = group.ToList();
            if (items.Count < 2) continue;
            var min = items.Min(x => x.Score);
            var max = items.Max(x => x.Score);
            var maxScale = Math.Max(1m, items[0].MaxScore);
            var spreadPct = (max - min) / maxScale * 100m;
            if (spreadPct >= 20m)
            {
                deterministicAlerts.Add(new ScoreCalibrationAlertDto(
                    spreadPct >= 30m ? "HIGH" : "MEDIUM",
                    "DISCREPANCY",
                    $"Chênh lệch điểm {Math.Round(spreadPct, 1)}% ở tiêu chí \"{group.Key}\"",
                    $"Điểm dao động từ {min:0.##} đến {max:0.##}/{maxScale:0.##} giữa các giám khảo ({string.Join("; ", items.Select(i => $"{i.Evaluator}: {i.Score:0.##}"))}).",
                    group.Key));
            }
        }

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_CALIBRATION] Rà soát các phiếu chấm hội đồng của đồ án để phát hiện lệch điểm bất thường, mâu thuẫn giữa điểm số và nhận xét văn bản, hoặc phiếu chấm thiếu nhận xét ở tiêu chí điểm thấp. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"overallStatus\":\"CONSISTENT\"|\"REVIEW_RECOMMENDED\"|\"HIGH_VARIANCE\",\"summary\":string,\"alerts\":[{\"severity\":\"HIGH\"|\"MEDIUM\"|\"LOW\",\"category\":\"DISCREPANCY\"|\"COMMENT_MISMATCH\"|\"INCOMPLETE\"|\"OUTLIER\",\"title\":string,\"detail\":string,\"criterionName\":string|null}],\"recommendations\":string[]}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            evaluations = evals.Select(e => new
            {
                e.Id,
                evaluator = e.Evaluator.FullName,
                e.EvaluationType,
                e.Status,
                e.TotalScore,
                e.Comments,
                details = e.EvaluationDetails.Select(d => new
                {
                    criterion = d.RubricCriterion.Criterion?.Name ?? $"Tiêu chí #{d.RubricCriterionId}",
                    d.Score,
                    maxScore = d.RubricCriterion.MaxScore ?? 10m,
                    d.Comments
                }).ToArray()
            }).ToArray(),
            detectedSpreadAlerts = deterministicAlerts
        }, ct);

        var status = ResolveGenerationStatus(true);
        var overallStatus = deterministicAlerts.Any(a => a.Severity == "HIGH") ? "HIGH_VARIANCE"
            : deterministicAlerts.Count > 0 ? "REVIEW_RECOMMENDED" : "CONSISTENT";
        var summary = evals.Count == 0
            ? "Đồ án chưa có phiếu chấm nào để đối chiếu."
            : $"Đã đối chiếu {evals.Count} phiếu chấm của đồ án {project.Code}.";
        var allAlerts = new List<ScoreCalibrationAlertDto>(deterministicAlerts);
        var recommendations = Array.Empty<string>();

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            var aiOverall = ReadString(r, "overallStatus", overallStatus, 30).ToUpperInvariant();
            if (aiOverall is "CONSISTENT" or "REVIEW_RECOMMENDED" or "HIGH_VARIANCE") overallStatus = aiOverall;
            summary = ReadString(r, "summary", summary, 1500);
            recommendations = ReadStringArray(r, "recommendations", 6, 300);

            if (r.TryGetProperty("alerts", out var aArr) && aArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in aArr.EnumerateArray().Take(10))
                {
                    var t = ReadString(a, "title", "", 200);
                    if (string.IsNullOrWhiteSpace(t) || allAlerts.Any(x => string.Equals(x.Title, t, StringComparison.OrdinalIgnoreCase))) continue;
                    var sev = ReadString(a, "severity", "MEDIUM", 12).ToUpperInvariant();
                    if (sev is not ("HIGH" or "MEDIUM" or "LOW")) sev = "MEDIUM";
                    var cat = ReadString(a, "category", "DISCREPANCY", 24).ToUpperInvariant();
                    if (cat is not ("DISCREPANCY" or "COMMENT_MISMATCH" or "INCOMPLETE" or "OUTLIER")) cat = "DISCREPANCY";
                    allAlerts.Add(new ScoreCalibrationAlertDto(sev, cat, t, ReadString(a, "detail", "", 500), ReadNullableString(a, "criterionName", 160)));
                }
            }
        }
        else
        {
            status = "FALLBACK";
        }

        var snapPayload = new { overallStatus, summary, alerts = allAlerts, recommendations };
        var meta = await SaveSnapshotAsync("SCORE_CALIBRATION", "PROJECT", projectId, projectId, null, status, $"Kiểm tra nhất quán điểm · {project.Code}", snapPayload, actor, actorName, ct);
        return new ScoreCalibrationDto(projectId, evals.Count, overallStatus, summary, allAlerts, recommendations, meta);
    }

    public async Task<AppealAssistDto> AssistAppealReviewAsync(long appealId, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var actor = RequireActor();
        if (!IsStaffOrAdmin)
            throw new ForbiddenException("Chỉ Cán bộ Khoa hoặc Quản trị viên mới có quyền sử dụng trợ lý AI giải trình khiếu nại.");

        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == actor).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? $"Cán bộ #{actor}";
        var appeal = await db.Set<ResultAppealRequest>().AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == appealId, ct)
            ?? throw new NotFoundException("ResultAppealRequest", appealId);

        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == appeal.ProjectId, ct)
            ?? throw new NotFoundException("Project", appeal.ProjectId);

        var evals = await db.Evaluations.AsNoTracking()
            .Include(e => e.Evaluator)
            .Include(e => e.EvaluationDetails).ThenInclude(d => d.RubricCriterion).ThenInclude(rc => rc.Criterion)
            .Where(e => e.ProjectId == appeal.ProjectId)
            .Take(8)
            .ToListAsync(ct);

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_APPEAL_ASSIST] Hỗ trợ Cán bộ Khoa/Hội đồng phúc khảo rà soát đơn khiếu nại điểm một cách khách quan, minh bạch. " +
            "Tóm tắt yêu cầu của sinh viên, liệt kê các điểm cần xác minh từ phiếu chấm gốc, và soạn bản nháp phản hồi chuẩn mực hành chính đại học. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"appealSummary\":string,\"meritAssessment\":\"LIKELY_MERIT\"|\"NEEDS_MANUAL_CHECK\"|\"WEAK_BASIS\",\"keyClaims\":string[],\"evidenceChecks\":string[],\"draftResponse\":string}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            appealId = appeal.Id,
            projectCode = project.Code,
            projectTitle = project.Title,
            appealStatus = appeal.Status,
            appealScope = appeal.Scope,
            reason = Clip(appeal.Reason, 2000),
            existingProposal = Clip(appeal.Proposal, 800),
            evaluations = evals.Select(e => new
            {
                evaluator = e.Evaluator.FullName,
                e.EvaluationType,
                e.TotalScore,
                e.Comments,
                details = e.EvaluationDetails.Select(d => new { criterion = d.RubricCriterion.Criterion?.Name, d.Score, d.Comments }).ToArray()
            }).ToArray()
        }, ct);

        var status = ResolveGenerationStatus(true);
        string appealSummary = $"Tóm tắt đơn khiếu nại #{appealId} của đồ án {project.Code}.";
        string merit = "NEEDS_MANUAL_CHECK";
        string[] claims = [Clip(appeal.Reason, 300)];
        string[] checks = ["Đối chiếu trực tiếp phiếu chấm gốc và biên bản hội đồng."];
        string draftResponse = "";

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            appealSummary = ReadString(r, "appealSummary", appealSummary, 1500);
            var m = ReadString(r, "meritAssessment", merit, 30).ToUpperInvariant();
            if (m is "LIKELY_MERIT" or "NEEDS_MANUAL_CHECK" or "WEAK_BASIS") merit = m;
            claims = ReadStringArray(r, "keyClaims", 6, 300);
            checks = ReadStringArray(r, "evidenceChecks", 6, 300);
            draftResponse = ReadString(r, "draftResponse", "", 3000);
        }
        else
        {
            status = "FALLBACK";
        }

        var snapPayload = new { appealSummary, merit, claims, checks, draftResponse };
        var meta = await SaveSnapshotAsync("APPEAL_ASSIST", "APPEAL", appealId, appeal.ProjectId, appeal.ResponsibleDepartmentId, status, $"Hỗ trợ khiếu nại #{appealId}", snapPayload, actor, actorName, ct);
        return new AppealAssistDto(appealId, appeal.ProjectId, appealSummary, merit, claims, checks, draftResponse, meta);
    }

    public async Task<EvaluationSchemeSuggestDto> SuggestEvaluationSchemeAsync(long projectId, CancellationToken ct)
    {
        var actor = RequireActor();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.FullName,
                u.DepartmentId,
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF")
            })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException("Tài khoản không hợp lệ.");

        if (!user.IsAdmin && !user.IsStaff)
            throw new ForbiddenException("Chỉ Cán bộ bộ môn hoặc Quản trị viên mới được dùng AI gợi ý Phổ điểm đánh giá.");

        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Title,
                SemesterId = p.Team.AcademicSemesterId,
                SemesterCode = p.Team.AcademicSemester.Code,
                OrganizationId = p.Team.AcademicSemester.OrganizationId,
                Majors = p.ProjectMajors.Select(pm => new { pm.MajorId, pm.Major.Code, pm.Major.Name, pm.Major.DepartmentId }).ToArray()
            })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);

        if (!user.IsAdmin && user.DepartmentId.HasValue
            && !project.Majors.Any(m => m.DepartmentId == user.DepartmentId.Value)
            && !await db.Set<ProjectRegistrationSnapshot>().AsNoTracking().AnyAsync(s => s.ProjectId == projectId && s.LeadDepartmentId == user.DepartmentId.Value, ct))
        {
            throw new ForbiddenException("Đồ án nằm ngoài phạm vi bộ môn được phân công của bạn.");
        }

        var projectMajorIds = project.Majors.Select(m => m.MajorId).ToHashSet();
        var projectDeptIds = project.Majors.Select(m => m.DepartmentId).Distinct().ToArray();

        // Load REAL published rubrics for this semester (or fallback to active org rubrics)
        var publishedIds = await db.Set<RubricVersion>().AsNoTracking()
            .Where(v => v.Status == "PUBLISHED")
            .Select(v => v.RubricId)
            .ToArrayAsync(ct);
        var publishedSet = publishedIds.ToHashSet();

        var allRubrics = await db.Rubrics.AsNoTracking()
            .Where(r => r.AcademicSemesterId == project.SemesterId
                && (projectDeptIds.Length == 0 || (r.DepartmentId.HasValue && projectDeptIds.Contains(r.DepartmentId.Value)) || r.ProjectMode == "INTERDISCIPLINARY"))
            .Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.MajorId,
                r.ProjectMode,
                r.EvaluationStage,
                r.EvaluationScope
            })
            .ToListAsync(ct);

        var eligibleRubrics = allRubrics
            .Where(r => publishedSet.Contains(r.Id))
            .Where(r => r.MajorId is null || projectMajorIds.Contains(r.MajorId.Value))
            .ToList();

        var warnings = new List<string>();
        var suggestedName = $"Phương án đánh giá {project.Code} ({project.SemesterCode})";
        decimal suggestedThreshold = 5.0m;
        var usedAi = false;
        string summary;
        var components = new List<EvaluationSchemeSuggestComponentDto>();

        if (eligibleRubrics.Count == 0)
        {
            warnings.Add($"Chưa tìm thấy Bộ tiêu chí (Rubric) ở trạng thái ĐÃ CÔNG BỐ (PUBLISHED) nào trong học kỳ {project.SemesterCode} cho các ngành của đồ án. Hãy công bố ít nhất 1 Rubric trước khi thiết lập Phổ điểm.");
            summary = $"Không thể phân bổ phổ điểm tự động vì học kỳ {project.SemesterCode} chưa có Rubric đã công bố phù hợp với đồ án {project.Code}.";
        }
        else
        {
            // Deterministic 100%-balanced baseline using ONLY real PUBLISHED rubrics
            var baseline = new List<EvaluationSchemeSuggestComponentDto>();
            var isInterdisciplinary = project.Majors.Length > 1;
            var stages = new[] { "ON_GOING", "COLD", "DEFENSE" };

            var stageRubrics = new Dictionary<string, (long Id, string Code, string Name)>();
            foreach (var st in stages)
            {
                var best = eligibleRubrics.FirstOrDefault(r =>
                        string.Equals(r.EvaluationStage, st, StringComparison.OrdinalIgnoreCase) && r.EvaluationScope == "COMMON")
                    ?? eligibleRubrics.FirstOrDefault(r => string.Equals(r.EvaluationStage, st, StringComparison.OrdinalIgnoreCase));
                if (best is not null) stageRubrics[st] = (best.Id, best.Code, best.Name);
            }

            if (stageRubrics.Count == 0)
            {
                var fallback = eligibleRubrics[0];
                stageRubrics["DEFENSE"] = (fallback.Id, fallback.Code, fallback.Name);
            }

            var stageCount = stageRubrics.Count;
            decimal WeightFor(string st) => stageCount switch
            {
                3 => st == "ON_GOING" ? 20m : st == "COLD" ? 30m : 50m,
                2 => st == "DEFENSE" ? 60m : st == "COLD" ? 50m : 40m,
                _ => 100m
            };

            if (stageCount == 2)
            {
                var keys = stageRubrics.Keys.ToArray();
                var w0 = WeightFor(keys[0]);
                var w1 = 100m - w0;
                var pairWeights = new Dictionary<string, decimal> { [keys[0]] = w0, [keys[1]] = w1 };
                foreach (var (st, rub) in stageRubrics)
                {
                    var w = pairWeights[st];
                    baseline.Add(new(
                        st == "ON_GOING" ? "Đánh giá quá trình" : st == "COLD" ? "Chấm nguội hồ sơ" : "Bảo vệ hội đồng",
                        "COMMON", null, null, rub.Id, rub.Code, rub.Name, st, w, w,
                        st == "DEFENSE" ? 3 : st == "COLD" ? 2 : 1,
                        $"Đề xuất từ Rubric đã công bố {rub.Code} (giai đoạn {st})."));
                }
            }
            else
            {
                foreach (var (st, rub) in stageRubrics)
                {
                    var w = WeightFor(st);
                    baseline.Add(new(
                        st == "ON_GOING" ? "Đánh giá quá trình" : st == "COLD" ? "Chấm nguội hồ sơ" : "Bảo vệ hội đồng",
                        "COMMON", null, null, rub.Id, rub.Code, rub.Name, st, w, w,
                        st == "DEFENSE" ? 3 : st == "COLD" ? 2 : 1,
                        $"Đề xuất từ Rubric đã công bố {rub.Code} (giai đoạn {st})."));
                }
            }

            if (isInterdisciplinary)
            {
                // If EVERY project major has its own MAJOR_SPECIFIC published rubric (e.g. COLD), offer a true interdisciplinary split!
                var perMajorRubrics = project.Majors.Select(m => new
                {
                    Major = m,
                    Rubric = eligibleRubrics.FirstOrDefault(r => r.EvaluationScope == "MAJOR_SPECIFIC" && r.MajorId == m.MajorId)
                }).ToArray();

                var commonDefense = stageRubrics.GetValueOrDefault("DEFENSE");
                if (commonDefense.Id > 0 && perMajorRubrics.All(x => x.Rubric is not null))
                {
                    var hybrid = new List<EvaluationSchemeSuggestComponentDto>();
                    var commonProjWeight = 60m;
                    var commonStudentWeight = 60m;
                    hybrid.Add(new("Bảo vệ hội đồng chung liên ngành", "COMMON", null, null,
                        commonDefense.Id, commonDefense.Code, commonDefense.Name, "DEFENSE",
                        commonProjWeight, commonStudentWeight, 3, "Thành phần chung đánh giá tích hợp liên ngành."));

                    var remProj = 100m - commonProjWeight;
                    var eachProj = Math.Floor(remProj / perMajorRubrics.Length);
                    for (var i = 0; i < perMajorRubrics.Length; i++)
                    {
                        var pm = perMajorRubrics[i];
                        var pWeight = i == perMajorRubrics.Length - 1 ? remProj - eachProj * (perMajorRubrics.Length - 1) : eachProj;
                        var st = pm.Rubric!.EvaluationStage is "ON_GOING" or "COLD" or "DEFENSE" ? pm.Rubric.EvaluationStage : "COLD";
                        hybrid.Add(new($"Đánh giá chuyên sâu {pm.Major.Code}", "MAJOR_SPECIFIC", pm.Major.MajorId, pm.Major.Name,
                            pm.Rubric.Id, pm.Rubric.Code, pm.Rubric.Name, st, pWeight, 100m - commonStudentWeight, 2,
                            $"Thành phần chuyên ngành {pm.Major.Name} dùng Rubric {pm.Rubric.Code}."));
                    }
                    baseline = hybrid;
                }
            }

            components = baseline;
            summary = $"Đề xuất {components.Count} thành phần đánh giá sử dụng 100% Rubric đã công bố trong học kỳ {project.SemesterCode} (tổng điểm đồ án = 100%, tổng điểm sinh viên mỗi ngành = 100%).";

            // Try LLM refinement if remote provider available, strictly validating against allowlisted rubricIds & 100% weights
            if (aiProvider is not null && !aiProvider.ModelVersion.StartsWith("GROUNDED_LOCAL", StringComparison.Ordinal))
            {
                var prompt = AiSuiteIdentity + " " +
                    "[AI_SUITE_EVAL_SCHEME_SUGGEST] Đề xuất phân bổ Phổ điểm đánh giá cuối kỳ cho đồ án chỉ sử dụng các rubricId thật trong availablePublishedRubrics. " +
                    "BẮT BUỘC: tổng projectWeightPercent = 100; với MỖI ngành trong projectMajors, tổng studentWeightPercent của các thành phần COMMON + MAJOR_SPECIFIC/INDIVIDUAL thuộc ngành đó = 100; scope='INDIVIDUAL' có projectWeightPercent=0; stage thuộc ON_GOING|COLD|DEFENSE; chỉ dùng rubricId có trong danh sách. " +
                    "Trả ONLY raw JSON: {\"suggestedName\":string,\"suggestedPassThreshold\":number,\"summary\":string,\"components\":[{\"name\":string,\"scope\":\"COMMON|MAJOR_SPECIFIC|INDIVIDUAL\",\"majorId\":number|null,\"rubricId\":number,\"stage\":\"ON_GOING|COLD|DEFENSE\",\"projectWeightPercent\":number,\"studentWeightPercent\":number,\"requiredEvaluators\":number,\"rationale\":string}],\"warnings\":string[]}.";
                try
                {
                    var raw = await CallAiAsync(prompt, new
                    {
                        projectCode = project.Code,
                        projectTitle = project.Title,
                        projectMajors = project.Majors,
                        availablePublishedRubrics = eligibleRubrics
                    }, ct);
                    using var doc = TryParseJsonObject(raw);
                    if (doc is not null)
                    {
                        var root = doc.RootElement;
                        var byRubricId = eligibleRubrics.ToDictionary(r => r.Id);
                        var majorById = project.Majors.ToDictionary(m => m.MajorId, m => m.Name);
                        var candidate = new List<EvaluationSchemeSuggestComponentDto>();
                        if (root.TryGetProperty("components", out var cArr) && cArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var el in cArr.EnumerateArray().Take(12))
                            {
                                if (el.ValueKind != JsonValueKind.Object) continue;
                                if (!el.TryGetProperty("rubricId", out var rEl) || !rEl.TryGetInt64(out var rid) || !byRubricId.TryGetValue(rid, out var rub)) continue;
                                var sc = ReadString(el, "scope", "COMMON", 24).ToUpperInvariant();
                                if (sc is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) continue;
                                long? mid = el.TryGetProperty("majorId", out var mEl) && mEl.ValueKind == JsonValueKind.Number && mEl.TryGetInt64(out var mval) ? mval : null;
                                if (sc == "COMMON") mid = null;
                                else if (!mid.HasValue || !majorById.ContainsKey(mid.Value)) continue;
                                var st = ReadString(el, "stage", rub.EvaluationStage ?? "DEFENSE", 24).ToUpperInvariant();
                                if (st is not ("ON_GOING" or "COLD" or "DEFENSE")) st = "DEFENSE";
                                var pw = ReadDecimal(el, "projectWeightPercent", 0m, 100m) ?? 0m;
                                var sw = ReadDecimal(el, "studentWeightPercent", 0m, 100m) ?? 0m;
                                if (sc == "INDIVIDUAL") pw = 0m;
                                var reqEval = el.TryGetProperty("requiredEvaluators", out var eEl) && eEl.ValueKind == JsonValueKind.Number && eEl.TryGetInt32(out var ev) ? Math.Clamp(ev, 1, 20) : 1;
                                candidate.Add(new(
                                    ReadString(el, "name", rub.Name, 200), sc, mid, mid.HasValue ? majorById[mid.Value] : null,
                                    rub.Id, rub.Code, rub.Name, st, pw, sw, reqEval,
                                    ReadString(el, "rationale", $"Sử dụng Rubric {rub.Code}.", 300)));
                            }
                        }

                        var projSumOk = candidate.Count > 0 && candidate.Sum(c => c.ProjectWeightPercent) == 100m;
                        var majorsSumOk = project.Majors.All(m =>
                            candidate.Where(c => c.Scope == "COMMON" || c.MajorId == m.MajorId).Sum(c => c.StudentWeightPercent) == 100m);

                        if (projSumOk && majorsSumOk)
                        {
                            components = candidate;
                            usedAi = true;
                            suggestedName = ReadString(root, "suggestedName", suggestedName, 200);
                            suggestedThreshold = ReadDecimal(root, "suggestedPassThreshold", 0m, 10m) ?? 5.0m;
                            summary = ReadString(root, "summary", summary, 1000);
                            foreach (var w in ReadStringArray(root, "warnings", 5, 300)) warnings.Add(w);
                        }
                        else
                        {
                            warnings.Add("Đề xuất từ LLM chưa khớp đúng tổng 100% ở mọi ngành nên hệ thống tự chuyển sang cấu trúc phân bổ chuẩn từ các Rubric đã công bố.");
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch
                {
                    warnings.Add("Mô hình AI tạm thời không phản hồi; hệ thống đã sinh phương án cân bằng 100% từ các Rubric đã công bố.");
                }
            }
        }

        var metaStatus = ResolveGenerationStatus(usedAi);
        var snapPayload = new { suggestedName, suggestedThreshold, summary, usedAi, components, warnings };
        var meta = await SaveSnapshotAsync("EVAL_SCHEME_SUGGEST", "PROJECT", projectId, projectId, user.DepartmentId,
            metaStatus, $"Gợi ý Phổ điểm {project.Code}", snapPayload, actor, user.FullName ?? $"User #{actor}", ct);
        return new EvaluationSchemeSuggestDto(projectId, suggestedName, suggestedThreshold, summary, usedAi, components, warnings, meta);
    }
}
