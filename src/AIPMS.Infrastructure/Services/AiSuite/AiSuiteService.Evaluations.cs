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

        var status = "COMPLETED";
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

        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == actor).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? $"Cán bộ #{actor}";
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

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

        var status = "COMPLETED";
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

        var status = "COMPLETED";
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
}
