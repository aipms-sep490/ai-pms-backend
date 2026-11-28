using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiSuite;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIPMS.Infrastructure.Services.AiSuite;

internal sealed partial class AiSuiteService
{
    private const string AntiAnchoringNeutralLevel = "Cần giám khảo tự đánh giá";

    public async Task<RubricGradingCopilotDto> GenerateRubricGradingCopilotAsync(long assignmentId, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var actor = RequireActor();
        EnsureNotPureAdmin("Quản trị viên không có thẩm quyền sử dụng AI Copilot chấm điểm Rubric.");

        var assignment = await db.Set<EvaluationAssignment>().AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new NotFoundException("EvaluationAssignment", assignmentId);

        if (assignment.EvaluatorId != actor)
            throw new ForbiddenException("Chỉ giám khảo được phân công chấm mới có quyền dùng AI Copilot cho phiếu chấm này.");

        var nowUtc = Now;
        if (!string.Equals(assignment.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
            || assignment.RevokedAt.HasValue
            || (assignment.ValidFrom.HasValue && nowUtc < assignment.ValidFrom.Value)
            || (assignment.ValidUntil.HasValue && nowUtc > assignment.ValidUntil.Value))
        {
            throw new ForbiddenException(
                "Phân công chấm điểm đã bị thu hồi hoặc nằm ngoài thời hạn hiệu lực.",
                WorkflowErrorCodes.AssignmentRevoked);
        }

        if (await AcademicAssignmentGuard.HasSupervisorOrMentorConflictAsync(db, assignment.ProjectId, actor, ct))
        {
            throw new ConflictException(
                "Giảng viên hướng dẫn hoặc mentor chuyên ngành của đồ án không được chấm điểm độc lập cho đồ án này (D17).",
                WorkflowErrorCodes.EvaluatorRoleConflict);
        }

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

        // Scope isolation for MAJOR_SPECIFIC / INDIVIDUAL evaluator assignments (TK-W3-03)
        var assignmentScope = (assignment.Scope ?? "COMMON").Trim().ToUpperInvariant();
        var deliverablesQuery = db.Deliverables.AsNoTracking()
            .Where(d => d.ProjectId == project.Id || (d.Milestone != null && d.Milestone.ProjectId == project.Id));
        var tasksQuery = db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt == null && t.Milestone.ProjectId == project.Id && t.Status != "CANCELLED");

        if (assignmentScope == "MAJOR_SPECIFIC" && assignment.MajorId is long evalMajorId)
        {
            deliverablesQuery = deliverablesQuery.Where(d =>
                d.EvidenceScope == null || d.EvidenceScope == "COMMON" || d.EvidenceScope == "INTEGRATION"
                || d.TargetMajorId == evalMajorId);
            tasksQuery = tasksQuery.Where(t =>
                db.TaskDisciplines.Any(td => td.TaskId == t.Id && td.MajorId == evalMajorId));
        }
        else if (assignmentScope == "INDIVIDUAL" && assignment.StudentId is long evalStudentId)
        {
            deliverablesQuery = deliverablesQuery.Where(d => d.TargetStudentId == null || d.TargetStudentId == evalStudentId);
            tasksQuery = tasksQuery.Where(t => t.TaskAssignees.Any(ta => ta.UserId == evalStudentId));
        }

        var scopedDeliverables = await deliverablesQuery
            .OrderByDescending(d => d.UpdatedAt)
            .Take(12)
            .Select(d => new { d.Id, d.Title, d.Status, Description = Clip(d.Description, 250) })
            .ToListAsync(ct);
        var allowedDelivIds = scopedDeliverables.Select(d => d.Id).ToHashSet();

        var finalSub = await db.Set<FinalSubmission>().AsNoTracking()
            .Include(f => f.Items)
            .Where(f => f.ProjectId == project.Id)
            .OrderByDescending(f => f.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        var scopedFinalItems = finalSub?.Items
            .Where(i => assignmentScope != "MAJOR_SPECIFIC" || allowedDelivIds.Count == 0 || allowedDelivIds.Contains(i.DeliverableId))
            .Select(i => new { i.Title, i.VersionNumber, i.StatusAtSubmission })
            .ToArray() ?? [];

        var reports = await db.ProgressReports.AsNoTracking()
            .Where(r => r.ProjectId == project.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(4)
            .Select(r => new { r.ReportType, Summary = Clip(r.Summary, 350), Issues = Clip(r.IssuesAndRisks, 250) })
            .ToListAsync(ct);

        var taskSummary = await tasksQuery
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
            "[AI_SUITE_RUBRIC_COPILOT] Hỗ trợ Giám khảo rà soát bằng chứng đồ án theo từng tiêu chí của Rubric. " +
            "CHÍNH SÁCH CHỐNG NEO ĐIỂM (ANTI-ANCHORING — BẮT BUỘC): TUYỆT ĐỐI KHÔNG đề xuất điểm số, khoảng điểm số hay mức xếp loại định lượng (luôn đặt suggestedScoreMin=null, suggestedScoreMax=null, suggestedLevel=\"Cần giám khảo tự đánh giá\"). " +
            "CHỈ tổng hợp bằng chứng liên quan, nêu nhận xét định tính trung lập và câu hỏi xác minh để Giám khảo tự quyết định điểm chính thức. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"overallSummary\":string,\"criteria\":[{\"rubricCriterionId\":number,\"suggestedLevel\":\"Cần giám khảo tự đánh giá\",\"suggestedScoreMin\":null,\"suggestedScoreMax\":null,\"suggestedComment\":string,\"evidenceCitations\":string[],\"confidence\":\"HIGH\"|\"MEDIUM\"|\"LOW\"}],\"verificationQuestions\":string[]}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            projectDescription = Clip(project.Description, 1000),
            rubricName = rubric.Name,
            evaluationStage = assignment.Stage,
            assignmentScope,
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
            deliverables = scopedDeliverables.Select(d => new { d.Title, d.Status, d.Description }).ToArray(),
            finalSubmissionItems = scopedFinalItems,
            reports,
            taskSummary,
            meetingSummaries
        }, ct);

        var status = ResolveGenerationStatus(true);
        var overallSummary = $"Đã đối chiếu hồ sơ đồ án {project.Code} với {leafCriteria.Count} tiêu chí của Rubric \"{rubric.Name}\" (chế độ chống neo điểm — chỉ tóm tắt bằng chứng).";
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
                    var conf = ReadString(item, "confidence", "MEDIUM", 12).ToUpperInvariant();
                    if (conf is not ("HIGH" or "MEDIUM" or "LOW")) conf = "MEDIUM";
                    // Anti-Anchoring (TK-W0-09): always strip numeric score bounds and rating bands.
                    suggestionMap[cid] = new RubricCriterionSuggestionDto(
                        cid,
                        rc.Criterion?.Name ?? $"Tiêu chí #{cid}",
                        rc.WeightPercent,
                        max,
                        AntiAnchoringNeutralLevel,
                        null,
                        null,
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
                    AntiAnchoringNeutralLevel,
                    null,
                    null,
                    "Chưa đủ bằng chứng tự động; giám khảo vui lòng kiểm chứng trực tiếp trên sản phẩm và phiên bảo vệ.",
                    [],
                    "LOW")).ToArray();

        const string disclaimer = "Chế độ Chống neo điểm (Anti-Anchoring): AI chỉ tổng hợp bằng chứng, nhận xét định tính và câu hỏi kiểm chứng — không gợi ý điểm số. Giám khảo hoàn toàn độc lập quyết định điểm chính thức.";
        var snapPayload = new { overallSummary, criteria = orderedSuggestions, verificationQuestions, antiAnchoringEnforced = true };
        var meta = await SaveSnapshotAsync("RUBRIC_GRADING", "EVALUATION", assignmentId, project.Id, assignment.DepartmentId, status, $"Copilot Rubric · {project.Code}", snapPayload, actor, actorName, ct);

        return new RubricGradingCopilotDto(
            assignmentId, project.Id, rubric.Name, overallSummary, orderedSuggestions, verificationQuestions, disclaimer, meta, AntiAnchoringEnforced: true);
    }

    public async Task<ScoreCalibrationDto> CheckScoreCalibrationAsync(long projectId, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var actor = RequireActor();
        RequireDepartmentStaffOnly("Chỉ Cán bộ Khoa chủ trì mới có quyền kiểm tra độ nhất quán điểm hội đồng.");

        var actorRecord = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new { u.FullName, u.DepartmentId })
            .FirstOrDefaultAsync(ct)
            ?? throw new ForbiddenException("Tài khoản không hoạt động.");
        var actorName = actorRecord.FullName ?? $"Cán bộ #{actor}";
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: false);
        var isLeadStaff = actorRecord.DepartmentId.HasValue
            && (scope.LeadDepartmentId.HasValue
                ? actorRecord.DepartmentId == scope.LeadDepartmentId
                : scope.DepartmentIds.Contains(actorRecord.DepartmentId.Value));
        if (!isLeadStaff)
        {
            throw new ForbiddenException(
                "Chỉ Cán bộ Khoa chủ trì đồ án mới được kiểm tra độ nhất quán điểm hội đồng.",
                WorkflowErrorCodes.ScopeAccessDenied);
        }

        var evals = await db.Evaluations.AsNoTracking()
            .Include(e => e.Evaluator)
            .Include(e => e.EvaluationDetails).ThenInclude(d => d.RubricCriterion).ThenInclude(rc => rc.Criterion)
            .Where(e => e.ProjectId == projectId && (e.Status == "SUBMITTED" || e.Status == "FINALIZED"))
            .ToListAsync(ct);

        // Deterministic variance calculation grouped by (Stage/EvaluationType, RubricId, RubricCriterionId)
        var deterministicAlerts = new List<ScoreCalibrationAlertDto>();
        var byCriterion = evals
            .SelectMany(e => e.EvaluationDetails.Select(d => new
            {
                Stage = e.EvaluationType,
                e.RubricId,
                d.RubricCriterionId,
                Evaluator = e.Evaluator.FullName,
                CriterionName = d.RubricCriterion.Criterion?.Name ?? $"Tiêu chí #{d.RubricCriterionId}",
                MaxScore = d.RubricCriterion.MaxScore ?? 10m,
                d.Score,
                d.Comments
            }))
            .GroupBy(x => (x.Stage, x.RubricId, x.RubricCriterionId));

        foreach (var group in byCriterion)
        {
            var items = group.ToList();
            if (items.Count < 2) continue;
            var criterionLabel = items[0].CriterionName;
            var min = items.Min(x => x.Score);
            var max = items.Max(x => x.Score);
            var maxScale = Math.Max(1m, items[0].MaxScore);
            var spreadPct = (max - min) / maxScale * 100m;
            if (spreadPct >= 20m)
            {
                deterministicAlerts.Add(new ScoreCalibrationAlertDto(
                    spreadPct >= 30m ? "HIGH" : "MEDIUM",
                    "DISCREPANCY",
                    $"Chênh lệch điểm {Math.Round(spreadPct, 1)}% ở tiêu chí \"{criterionLabel}\" ({group.Key.Stage})",
                    $"Điểm dao động từ {min:0.##} đến {max:0.##}/{maxScale:0.##} giữa các giám khảo ({string.Join("; ", items.Select(i => $"{i.Evaluator}: {i.Score:0.##}"))}).",
                    criterionLabel));
            }
        }

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_CALIBRATION] Rà soát các phiếu chấm hội đồng đã nộp/chốt của đồ án để phát hiện lệch điểm bất thường, mâu thuẫn giữa điểm số và nhận xét văn bản, hoặc phiếu chấm thiếu nhận xét ở tiêu chí điểm thấp. " +
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
                e.RubricId,
                e.Status,
                e.TotalScore,
                e.Comments,
                details = e.EvaluationDetails.Select(d => new
                {
                    d.RubricCriterionId,
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
            ? "Đồ án chưa có phiếu chấm nào ở trạng thái đã nộp/chốt để đối chiếu."
            : $"Đã đối chiếu {evals.Count} phiếu chấm đã nộp/chốt của đồ án {project.Code}.";
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
        var meta = await SaveSnapshotAsync("SCORE_CALIBRATION", "PROJECT", projectId, projectId, actorRecord.DepartmentId, status, $"Kiểm tra nhất quán điểm · {project.Code}", snapPayload, actor, actorName, ct);
        return new ScoreCalibrationDto(projectId, evals.Count, overallStatus, summary, allAlerts, recommendations, meta);
    }

    public async Task<AppealAssistDto> AssistAppealReviewAsync(long appealId, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var actor = RequireActor();
        EnsureNotPureAdmin("Quản trị viên không có thẩm quyền sử dụng trợ lý AI giải trình khiếu nại.");

        var actorRecord = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new { u.FullName, u.DepartmentId })
            .FirstOrDefaultAsync(ct)
            ?? throw new ForbiddenException("Tài khoản không hoạt động.");
        var actorName = actorRecord.FullName ?? $"Cán bộ #{actor}";

        var appeal = await db.Set<ResultAppealRequest>().AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == appealId, ct)
            ?? throw new NotFoundException("ResultAppealRequest", appealId);

        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == appeal.ProjectId, ct)
            ?? throw new NotFoundException("Project", appeal.ProjectId);

        var projectScope = await ProjectAcademicScopeReader.ReadAsync(db, appeal.ProjectId, ct, requireFrozenScope: false);
        var isResponsibleOrLeadStaff = IsDepartmentStaff
            && actorRecord.DepartmentId.HasValue
            && (actorRecord.DepartmentId.Value == appeal.ResponsibleDepartmentId
                || (projectScope.LeadDepartmentId.HasValue && actorRecord.DepartmentId.Value == projectScope.LeadDepartmentId.Value));

        var isAssignedReviewer = appeal.ReviewerId.HasValue && appeal.ReviewerId.Value == actor;
        if (isAssignedReviewer && await AcademicAssignmentGuard.HasSupervisorOrMentorConflictAsync(db, appeal.ProjectId, actor, ct))
        {
            throw new ConflictException(
                "Giảng viên hướng dẫn hoặc mentor chuyên ngành không thể phúc khảo độc lập cho đồ án này (D17).",
                WorkflowErrorCodes.EvaluatorRoleConflict);
        }

        var appealKind = string.IsNullOrWhiteSpace(appeal.AppealKind)
            ? "INDEPENDENT_80"
            : appeal.AppealKind.Trim().ToUpperInvariant();

        var isPrimarySupervisorForProcess20 = string.Equals(appealKind, "PROCESS_20_ERRATA", StringComparison.OrdinalIgnoreCase)
            && await db.SupervisorAssignments.AsNoTracking().AnyAsync(a =>
                a.ProjectId == appeal.ProjectId
                && a.EndedAt == null
                && a.AssignmentType == "PRIMARY"
                && a.IsPrimary
                && a.SupervisorProfile.UserId == actor, ct);

        if (!isResponsibleOrLeadStaff && !isAssignedReviewer && !isPrimarySupervisorForProcess20)
        {
            throw new ForbiddenException(
                "Bạn không có quyền sử dụng trợ lý AI giải trình đơn khiếu nại này.",
                WorkflowErrorCodes.ScopeAccessDenied);
        }

        // Load QD-04 scope-specific context (anonymizing evaluator names for blind review)
        object scopeEvidence;
        if (appealKind == "PROCESS_20_ERRATA")
        {
            var gateScores = await db.Set<GateIndividualProcessScore>().AsNoTracking()
                .Where(g => g.ProjectId == appeal.ProjectId && (!appeal.StudentId.HasValue || g.StudentId == appeal.StudentId.Value))
                .OrderBy(g => g.GateInstanceId)
                .Select(g => new { g.GateInstanceId, g.StudentId, g.Score, g.GateWeightPercent, g.Status, Comments = Clip(g.Comments, 300), MentorNotes = Clip(g.MentorReferenceNotes, 300) })
                .ToListAsync(ct);
            scopeEvidence = new
            {
                appealKind,
                supervisorNote = Clip(appeal.SupervisorNote, 800),
                gateProcessScores = gateScores
            };
        }
        else if (appealKind == "DEFENSE_ABSENT_CONTEST")
        {
            var absences = await db.Set<StudentDefenseAbsence>().AsNoTracking()
                .Where(a => a.ProjectId == appeal.ProjectId && (!appeal.StudentId.HasValue || a.StudentId == appeal.StudentId.Value))
                .OrderByDescending(a => a.RecordedAt)
                .Select(a => new { a.StudentId, a.Reason, a.MinutesReference, a.RecordedAt, a.RevokedAt, a.RevokedReason })
                .ToListAsync(ct);
            scopeEvidence = new
            {
                appealKind,
                defenseAbsences = absences
            };
        }
        else
        {
            var evals = await db.Evaluations.AsNoTracking()
                .Include(e => e.EvaluationDetails).ThenInclude(d => d.RubricCriterion).ThenInclude(rc => rc.Criterion)
                .Where(e => e.ProjectId == appeal.ProjectId && (e.Status == "SUBMITTED" || e.Status == "FINALIZED"))
                .OrderBy(e => e.Id)
                .Take(8)
                .ToListAsync(ct);

            var publishedSnapshot = appeal.StudentId.HasValue
                ? await db.Set<StudentResult>().AsNoTracking()
                    .Where(sr => sr.ProjectId == appeal.ProjectId && sr.StudentId == appeal.StudentId.Value)
                    .OrderByDescending(sr => sr.PublishedAt)
                    .Select(sr => Clip(sr.SnapshotJson, 1500))
                    .FirstOrDefaultAsync(ct)
                : null;

            scopeEvidence = new
            {
                appealKind,
                frozenInputs = Clip(appeal.FrozenInputs, 1800),
                publishedResultSnapshot = publishedSnapshot,
                evaluations = evals.Select((e, idx) => new
                {
                    evaluatorAlias = $"Giám khảo #{idx + 1}",
                    e.EvaluationType,
                    e.TotalScore,
                    e.Comments,
                    details = e.EvaluationDetails.Select(d => new { criterion = d.RubricCriterion.Criterion?.Name, d.Score, d.Comments }).ToArray()
                }).ToArray()
            };
        }

        var systemPrompt = AiSuiteIdentity + " " +
            $"[AI_SUITE_APPEAL_ASSIST] Hỗ trợ Cán bộ Khoa / Hội đồng phúc khảo rà soát đơn khiếu nại (loại {appealKind}) một cách khách quan, minh bạch theo QĐ-04. " +
            "CHÍNH SÁCH CHỐNG NEO ĐIỂM (BẮT BUỘC): TUYỆT ĐỐI KHÔNG phán quyết đơn đúng hay sai (luôn đặt meritAssessment=\"NEEDS_MANUAL_CHECK\") và KHÔNG tiết lộ danh tính cá nhân giám khảo chấm gốc. " +
            "CHỈ tóm tắt yêu cầu của sinh viên, liệt kê các điểm cần kiểm tra đối chiếu từ hồ sơ gốc và soạn bản nháp phản hồi hành chính trung lập. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"appealSummary\":string,\"meritAssessment\":\"NEEDS_MANUAL_CHECK\",\"keyClaims\":string[],\"evidenceChecks\":string[],\"draftResponse\":string}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            appealId = appeal.Id,
            appealKind,
            projectCode = project.Code,
            projectTitle = project.Title,
            appealStatus = appeal.Status,
            appealScope = appeal.Scope,
            reason = Clip(appeal.Reason, 2000),
            existingProposal = Clip(appeal.Proposal, 800),
            scopeEvidence
        }, ct);

        var status = ResolveGenerationStatus(true);
        string appealSummary = $"Tóm tắt đơn khiếu nại #{appealId} ({appealKind}) của đồ án {project.Code}.";
        const string merit = "NEEDS_MANUAL_CHECK"; // Anti-anchoring: always neutral for human review.
        string[] claims = [Clip(appeal.Reason, 300)];
        string[] checks = appealKind switch
        {
            "PROCESS_20_ERRATA" => ["Đối chiếu lại bảng điểm quá trình cá nhân tại 3 cổng G1 (20%), G2 (35%), G3 (45%) và xác nhận sai sót nhập liệu từ GVHD chính."],
            "DEFENSE_ABSENT_CONTEST" => ["Đối chiếu biên bản ghi nhận vắng bảo vệ, minh chứng điểm danh và đơn giải trình bất khả kháng của sinh viên."],
            _ => ["Đối chiếu ẩn danh phiếu chấm gốc (80% hội đồng độc lập), snapshot công bố kết quả và biên bản bảo vệ."]
        };
        string draftResponse = "";

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            appealSummary = ReadString(r, "appealSummary", appealSummary, 1500);
            claims = ReadStringArray(r, "keyClaims", 6, 300);
            checks = ReadStringArray(r, "evidenceChecks", 6, 300);
            draftResponse = ReadString(r, "draftResponse", "", 3000);
        }
        else
        {
            status = "FALLBACK";
        }

        var snapPayload = new { appealKind, appealSummary, merit, claims, checks, draftResponse };
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

        if (!user.IsStaff)
        {
            throw new ForbiddenException(
                user.IsAdmin
                    ? "Quản trị viên không có vai trò Cán bộ bộ môn (DEPARTMENT_STAFF) nên không được gợi ý/cấu hình Phổ điểm đánh giá."
                    : "Chỉ Cán bộ bộ môn mới được dùng AI gợi ý Phổ điểm đánh giá.",
                user.IsAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : null);
        }

        if (!user.DepartmentId.HasValue)
            throw new ForbiddenException("Tài khoản Cán bộ bộ môn chưa được gắn Khoa/Bộ môn quản lý.", WorkflowErrorCodes.ScopeAccessDenied);

        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Title,
                p.TrackCode,
                SemesterId = p.Team.AcademicSemesterId,
                SemesterCode = p.Team.AcademicSemester.Code,
                OrganizationId = p.Team.AcademicSemester.OrganizationId,
                Majors = p.ProjectMajors.Select(pm => new { pm.MajorId, pm.Major.Code, pm.Major.Name, pm.Major.DepartmentId }).ToArray()
            })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);

        if (!project.Majors.Any(m => m.DepartmentId == user.DepartmentId.Value)
            && !await db.Set<ProjectRegistrationSnapshot>().AsNoTracking().AnyAsync(s => s.ProjectId == projectId && s.LeadDepartmentId == user.DepartmentId.Value, ct))
        {
            throw new ForbiddenException("Đồ án nằm ngoài phạm vi bộ môn được phân công của bạn.", WorkflowErrorCodes.ScopeAccessDenied);
        }

        var projectMajorIds = project.Majors.Select(m => m.MajorId).ToHashSet();
        var projectDeptIds = project.Majors.Select(m => m.DepartmentId).Distinct().ToArray();
        var isInterdisciplinary = string.Equals(project.TrackCode, "IT_MKT", StringComparison.OrdinalIgnoreCase)
            || project.Majors.Length > 1;
        var effectiveTrackCode = !string.IsNullOrWhiteSpace(project.TrackCode)
            ? project.TrackCode!.Trim().ToUpperInvariant()
            : isInterdisciplinary ? "IT_MKT" : "SINGLE_MAJOR";

        // Load REAL published rubrics for this semester
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
        var suggestedName = $"Phương án đánh giá {project.Code} ({project.SemesterCode} · POL_V4_DECIMAL)";
        const decimal suggestedThreshold = 5.0m;
        const decimal failingComponentFloor = 4.00m;
        const string calculationVersion = "POL_V4_DECIMAL";
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
            // Deterministic POL_V4_DECIMAL baseline (POL-01 / POL-03 / POL-05)
            var baseline = new List<EvaluationSchemeSuggestComponentDto>();
            var coldCommon = eligibleRubrics.FirstOrDefault(r => string.Equals(r.EvaluationStage, "COLD", StringComparison.OrdinalIgnoreCase) && r.EvaluationScope == "COMMON")
                ?? eligibleRubrics.FirstOrDefault(r => string.Equals(r.EvaluationStage, "COLD", StringComparison.OrdinalIgnoreCase))
                ?? eligibleRubrics[0];
            var defenseCommon = eligibleRubrics.FirstOrDefault(r => string.Equals(r.EvaluationStage, "DEFENSE", StringComparison.OrdinalIgnoreCase) && r.EvaluationScope == "COMMON")
                ?? eligibleRubrics.FirstOrDefault(r => string.Equals(r.EvaluationStage, "DEFENSE", StringComparison.OrdinalIgnoreCase))
                ?? eligibleRubrics[0];

            if (!string.Equals(coldCommon.EvaluationStage, "COLD", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Chưa có Rubric công bố giai đoạn COLD; tạm ánh xạ {coldCommon.Code} làm bản nháp để Khoa điều chỉnh.");
            if (!string.Equals(defenseCommon.EvaluationStage, "DEFENSE", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Chưa có Rubric công bố giai đoạn DEFENSE; tạm ánh xạ {defenseCommon.Code} làm bản nháp để Khoa điều chỉnh.");

            if (!isInterdisciplinary)
            {
                // Single-major POL-01/POL-03: 30% COLD (Project 37.5%), 50% DEFENSE (Project 62.5%), 20% INDIVIDUAL/ON_GOING (Project 0%, no Rubric)
                baseline.Add(new(
                    "Chấm nguội hồ sơ (COLD 30%)",
                    "COMMON", null, null, coldCommon.Id, coldCommon.Code, coldCommon.Name, "COLD",
                    37.5m, 30m, 2,
                    $"POL-01: Chấm nguội chiếm 30% điểm sinh viên (37.5% phần 80% đồ án), dùng Rubric {coldCommon.Code}, tối thiểu 2 giám khảo độc lập.",
                    IsIntegration: false));
                baseline.Add(new(
                    "Bảo vệ hội đồng (DEFENSE 50%)",
                    "COMMON", null, null, defenseCommon.Id, defenseCommon.Code, defenseCommon.Name, "DEFENSE",
                    62.5m, 50m, 3,
                    $"POL-01: Bảo vệ hội đồng chiếm 50% điểm sinh viên (62.5% phần 80% đồ án), dùng Rubric {defenseCommon.Code}, hội đồng >= 3 giám khảo.",
                    IsIntegration: false));

                var singleMajor = project.Majors.FirstOrDefault();
                baseline.Add(new(
                    "Điểm quá trình cá nhân (POL-03 · 20%)",
                    "INDIVIDUAL", singleMajor?.MajorId, singleMajor?.Name,
                    0L, "POL-03", "Điểm quá trình cá nhân 3 cổng G1(20%)/G2(35%)/G3(45%)", "ON_GOING",
                    0m, 20m, 0,
                    "POL-03: 20% điểm quá trình cá nhân do GVHD chính chốt qua 3 Gate (G1=20%, G2=35%, G3=45%), không sử dụng Rubric hội đồng (RubricId=0, RequiredEvaluators=0).",
                    IsIntegration: false));
            }
            else
            {
                // Interdisciplinary (IT_MKT) POL-01/POL-03/POL-05:
                // 12% COMMON COLD (Project 15%, 2 evals) + 28% COMMON DEFENSE Integration (Project 35%, 3 evals, IsIntegration=true)
                // + 40% MAJOR_SPECIFIC per major (Project 50%/majorCount, 2 evals) + 20% INDIVIDUAL/ON_GOING per major (Project 0%, no rubric)
                baseline.Add(new(
                    "Chấm nguội chung liên ngành (COMMON COLD 12%)",
                    "COMMON", null, null, coldCommon.Id, coldCommon.Code, coldCommon.Name, "COLD",
                    15m, 12m, 2,
                    $"POL-05: Chấm nguội chung chiếm 12% điểm sinh viên (15% điểm đồ án), dùng Rubric {coldCommon.Code}, >= 2 giám khảo.",
                    IsIntegration: false));
                baseline.Add(new(
                    "Bảo vệ tích hợp liên ngành (COMMON DEFENSE 28%)",
                    "COMMON", null, null, defenseCommon.Id, defenseCommon.Code, defenseCommon.Name, "DEFENSE",
                    35m, 28m, 3,
                    $"POL-05: Bảo vệ tích hợp liên ngành chiếm 28% điểm sinh viên (35% điểm đồ án, IsIntegration=true, sàn liệt riêng >= 4.00), dùng Rubric {defenseCommon.Code}.",
                    IsIntegration: true));

                var majorsArr = project.Majors;
                var remProj = 50m;
                var eachProj = Math.Round(remProj / majorsArr.Length, 2);
                for (var i = 0; i < majorsArr.Length; i++)
                {
                    var m = majorsArr[i];
                    var pWeight = i == majorsArr.Length - 1
                        ? remProj - eachProj * (majorsArr.Length - 1)
                        : eachProj;
                    var majorRubric = eligibleRubrics.FirstOrDefault(r => r.EvaluationScope == "MAJOR_SPECIFIC" && r.MajorId == m.MajorId)
                        ?? eligibleRubrics.FirstOrDefault(r => r.MajorId == m.MajorId)
                        ?? defenseCommon;
                    if (majorRubric.MajorId != m.MajorId)
                        warnings.Add($"Chưa có Rubric MAJOR_SPECIFIC đã công bố riêng cho ngành {m.Name} ({m.Code}); tạm dùng {majorRubric.Code} để Khoa thay thế.");
                    var st = majorRubric.EvaluationStage is "COLD" or "DEFENSE" ? majorRubric.EvaluationStage : "DEFENSE";
                    baseline.Add(new(
                        $"Đánh giá chuyên ngành {m.Code} (40%)",
                        "MAJOR_SPECIFIC", m.MajorId, m.Name,
                        majorRubric.Id, majorRubric.Code, majorRubric.Name, st,
                        pWeight, 40m, 2,
                        $"POL-05: Thành phần chuyên ngành {m.Name} chiếm 40% điểm sinh viên ({pWeight:0.##}% điểm đồ án), dùng Rubric {majorRubric.Code}, >= 2 giám khảo chuyên ngành.",
                        IsIntegration: false));
                }

                foreach (var m in majorsArr)
                {
                    baseline.Add(new(
                        $"Điểm quá trình cá nhân {m.Code} (POL-03 · 20%)",
                        "INDIVIDUAL", m.MajorId, m.Name,
                        0L, "POL-03", "Điểm quá trình cá nhân 3 cổng G1(20%)/G2(35%)/G3(45%)", "ON_GOING",
                        0m, 20m, 0,
                        $"POL-03: 20% điểm quá trình cá nhân cho sinh viên ngành {m.Name} do GVHD chính chốt qua 3 Gate (20/35/45), không dùng Rubric hội đồng.",
                        IsIntegration: false));
                }
            }

            components = baseline;
            summary = isInterdisciplinary
                ? $"Đề xuất cấu trúc POL_V4_DECIMAL liên ngành ({effectiveTrackCode}): 12% COMMON COLD + 28% COMMON DEFENSE (tích hợp) + 40% chuyên ngành + 20% quá trình cá nhân POL-03 (sàn liệt từng cấu phần >= 4.00, điểm đạt >= 5.00)."
                : $"Đề xuất cấu trúc POL_V4_DECIMAL đơn ngành ({effectiveTrackCode}): 30% COLD + 50% DEFENSE + 20% quá trình cá nhân POL-03 (sàn liệt từng cấu phần >= 4.00, điểm đạt >= 5.00).";

            // Try LLM refinement if remote provider available, strictly validating against POL_V4_DECIMAL & allowlisted rubrics
            if (aiProvider is not null && !aiProvider.ModelVersion.StartsWith("GROUNDED_LOCAL", StringComparison.Ordinal))
            {
                var prompt = AiSuiteIdentity + " " +
                    "[AI_SUITE_EVAL_SCHEME_SUGGEST] Đề xuất phân bổ Phổ điểm đánh giá cuối kỳ theo chuẩn POL_V4_DECIMAL (POL-01/POL-03/POL-05) chỉ sử dụng rubricId thật trong availablePublishedRubrics. " +
                    "QUY TẮC BẮT BUỘC: " +
                    "1) Đơn ngành (IT_ONLY/MKT_ONLY): 30% COMMON COLD (projectWeightPercent=37.5, requiredEvaluators>=2) + 50% COMMON DEFENSE (projectWeightPercent=62.5, requiredEvaluators>=3) + 20% INDIVIDUAL ON_GOING (projectWeightPercent=0, rubricId=0, requiredEvaluators=0). " +
                    "2) Liên ngành (IT_MKT): 12% COMMON COLD (projectWeightPercent=15, requiredEvaluators>=2) + 28% COMMON DEFENSE (projectWeightPercent=35, requiredEvaluators>=3, isIntegration=true) + 40% MAJOR_SPECIFIC mỗi ngành (tổng projectWeightPercent=50, requiredEvaluators>=2) + 20% INDIVIDUAL ON_GOING mỗi ngành (projectWeightPercent=0, rubricId=0, requiredEvaluators=0). " +
                    "3) Tổng projectWeightPercent = 100; với MỖI ngành, tổng studentWeightPercent (COMMON + MAJOR_SPECIFIC + INDIVIDUAL của ngành) = 100; passThreshold = 5.0; sàn liệt cấu phần = 4.00. " +
                    "Trả ONLY raw JSON: {\"suggestedName\":string,\"suggestedPassThreshold\":5.0,\"summary\":string,\"components\":[{\"name\":string,\"scope\":\"COMMON|MAJOR_SPECIFIC|INDIVIDUAL\",\"majorId\":number|null,\"rubricId\":number,\"stage\":\"ON_GOING|COLD|DEFENSE\",\"projectWeightPercent\":number,\"studentWeightPercent\":number,\"requiredEvaluators\":number,\"isIntegration\":boolean,\"rationale\":string}],\"warnings\":string[]}.";
                try
                {
                    var raw = await CallAiAsync(prompt, new
                    {
                        projectCode = project.Code,
                        projectTitle = project.Title,
                        trackCode = effectiveTrackCode,
                        isInterdisciplinary,
                        calculationVersion,
                        failingComponentFloor,
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
                                var sc = ReadString(el, "scope", "COMMON", 24).ToUpperInvariant();
                                if (sc is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) continue;
                                long? mid = el.TryGetProperty("majorId", out var mEl) && mEl.ValueKind == JsonValueKind.Number && mEl.TryGetInt64(out var mval) ? mval : null;
                                if (sc == "COMMON") mid = null;
                                else if (!mid.HasValue || !majorById.ContainsKey(mid.Value))
                                {
                                    if (sc == "INDIVIDUAL" && !isInterdisciplinary && project.Majors.Length == 1)
                                        mid = project.Majors[0].MajorId;
                                    else
                                        continue;
                                }
                                var st = ReadString(el, "stage", "DEFENSE", 24).ToUpperInvariant();
                                if (st is not ("ON_GOING" or "COLD" or "DEFENSE")) st = "DEFENSE";
                                var pw = ReadDecimal(el, "projectWeightPercent", 0m, 100m) ?? 0m;
                                var sw = ReadDecimal(el, "studentWeightPercent", 0m, 100m) ?? 0m;
                                var isInteg = el.TryGetProperty("isIntegration", out var intEl) && intEl.ValueKind == JsonValueKind.True;

                                if (sc == "INDIVIDUAL" && st == "ON_GOING")
                                {
                                    candidate.Add(new(
                                        ReadString(el, "name", "Điểm quá trình cá nhân (POL-03 · 20%)", 200),
                                        "INDIVIDUAL", mid, mid.HasValue ? majorById[mid.Value] : null,
                                        0L, "POL-03", "Điểm quá trình cá nhân 3 cổng G1(20%)/G2(35%)/G3(45%)", "ON_GOING",
                                        0m, sw, 0,
                                        ReadString(el, "rationale", "POL-03: Điểm quá trình cá nhân 20% qua 3 Gate (20/35/45).", 300),
                                        IsIntegration: false));
                                    continue;
                                }

                                if (!el.TryGetProperty("rubricId", out var rEl) || !rEl.TryGetInt64(out var rid) || !byRubricId.TryGetValue(rid, out var rub)) continue;
                                if (sc == "INDIVIDUAL") pw = 0m;
                                var reqEval = el.TryGetProperty("requiredEvaluators", out var eEl) && eEl.ValueKind == JsonValueKind.Number && eEl.TryGetInt32(out var ev)
                                    ? Math.Clamp(ev, st == "DEFENSE" && sc == "COMMON" ? 3 : 2, 20)
                                    : (st == "DEFENSE" && sc == "COMMON" ? 3 : 2);
                                candidate.Add(new(
                                    ReadString(el, "name", rub.Name, 200), sc, mid, mid.HasValue ? majorById[mid.Value] : null,
                                    rub.Id, rub.Code, rub.Name, st, pw, sw, reqEval,
                                    ReadString(el, "rationale", $"Sử dụng Rubric {rub.Code}.", 300),
                                    IsIntegration: isInteg));
                            }
                        }

                        var projSumOk = candidate.Count > 0 && candidate.Sum(c => c.ProjectWeightPercent) == 100m;
                        var majorsSumOk = project.Majors.All(m =>
                            candidate.Where(c => c.Scope == "COMMON" || c.MajorId == m.MajorId).Sum(c => c.StudentWeightPercent) == 100m
                            && candidate.Any(c => c.Scope == "INDIVIDUAL" && c.Stage == "ON_GOING" && c.MajorId == m.MajorId && c.StudentWeightPercent == 20m));
                        var integrationOk = !isInterdisciplinary || candidate.Any(c => c.Scope == "COMMON" && c.Stage == "DEFENSE" && c.IsIntegration);

                        if (projSumOk && majorsSumOk && integrationOk)
                        {
                            components = candidate;
                            usedAi = true;
                            suggestedName = ReadString(root, "suggestedName", suggestedName, 200);
                            summary = ReadString(root, "summary", summary, 1000);
                            foreach (var w in ReadStringArray(root, "warnings", 5, 300)) warnings.Add(w);
                        }
                        else
                        {
                            warnings.Add("Đề xuất từ LLM chưa khớp hoàn toàn chuẩn POL_V4_DECIMAL (tổng 100%, 20% quá trình POL-03 và thành phần tích hợp) nên hệ thống đã áp dụng phương án chuẩn từ các Rubric đã công bố.");
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Evaluation scheme AI suggest failed; using POL_V4_DECIMAL baseline.");
                    warnings.Add("Mô hình AI tạm thời không phản hồi; hệ thống đã sinh phương án POL_V4_DECIMAL chuẩn từ các Rubric đã công bố.");
                }
            }
        }

        var metaStatus = ResolveGenerationStatus(usedAi);
        var snapPayload = new { suggestedName, suggestedThreshold, calculationVersion, failingComponentFloor, trackCode = effectiveTrackCode, summary, usedAi, components, warnings };
        var meta = await SaveSnapshotAsync("EVAL_SCHEME_SUGGEST", "PROJECT", projectId, projectId, user.DepartmentId,
            metaStatus, $"Gợi ý Phổ điểm {project.Code}", snapPayload, actor, user.FullName ?? $"User #{actor}", ct);
        return new EvaluationSchemeSuggestDto(
            projectId, suggestedName, suggestedThreshold, summary, usedAi, components, warnings, meta,
            CalculationVersion: calculationVersion,
            FailingComponentFloor: failingComponentFloor,
            TrackCode: effectiveTrackCode);
    }
}
