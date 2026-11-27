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
    private const string FeatureDefensePrep = "DEFENSE_PREP";
    private const string FeatureGateScript = "GATE_SCRIPT_DRAFT";

    public async Task<DefenseMockDto?> GetLatestDefenseMockAsync(long projectId, CancellationToken ct)
    {
        await RequireProjectAccessAsync(projectId, ct);
        var snap = await LoadLatestSnapshotAsync(FeatureDefensePrep, "PROJECT", projectId, ct);
        if (snap is null) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<DefenseMockPayload>(snap.Value.Row.PayloadJson, JsonOptions);
            if (payload is null) return null;
            return new DefenseMockDto(
                projectId,
                payload.Headline,
                payload.ReadinessAssessment,
                payload.WeakSpots ?? [],
                payload.Questions ?? [],
                BuildSnapshotMeta(snap.Value.Row.Id, FeatureDefensePrep, snap.Value.Row.Status, snap.Value.Row.ModelVersion, snap.Value.Row.CreatedAt, snap.Value.CreatorName));
        }
        catch (JsonException) { return null; }
    }

    public async Task<DefenseMockDto> GenerateDefenseMockAsync(long projectId, DefenseMockRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var (project, actor, actorName) = await RequireProjectAccessAsync(projectId, ct);
        var count = Math.Clamp(request.QuestionCount, 3, 12);
        var roleFocus = string.IsNullOrWhiteSpace(request.RoleFocus) ? "ALL" : request.RoleFocus.Trim().ToUpperInvariant();

        var members = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == project.TeamId && m.LeftAt == null)
            .Select(m => new { m.UserId, Name = m.User.FullName, Role = m.IsLeader ? "Trưởng nhóm" : "Thành viên", Major = m.User.Major != null ? m.User.Major.Name : null })
            .ToListAsync(ct);

        var deliverables = await db.Deliverables.AsNoTracking()
            .Where(d => d.Milestone != null && d.Milestone.ProjectId == projectId)
            .OrderByDescending(d => d.UpdatedAt)
            .Take(12)
            .Select(d => new { d.Title, d.Status, d.Description })
            .ToListAsync(ct);

        var finalSub = await db.Set<FinalSubmission>().AsNoTracking()
            .Include(f => f.Items)
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        var recentReports = await db.ProgressReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .Select(r => new { r.ReportType, Summary = Clip(r.Summary, 400), Issues = Clip(r.IssuesAndRisks, 300) })
            .ToListAsync(ct);

        var feedbacks = await db.SupervisorFeedbacks.AsNoTracking()
            .Where(f => (f.ProgressReport != null && f.ProgressReport.ProjectId == projectId) || (f.Meeting != null && f.Meeting.ProjectId == projectId))
            .OrderByDescending(f => f.CreatedAt)
            .Take(8)
            .Select(f => Clip(f.FeedbackText, 300))
            .ToListAsync(ct);

        var overdueTasks = await db.Tasks.AsNoTracking()
            .Where(t => t.Milestone.ProjectId == projectId && t.Status != "DONE" && t.Status != "CANCELLED")
            .OrderBy(t => t.DueAt ?? DateTime.MaxValue)
            .Take(12)
            .Select(t => new { t.Title, t.Status, t.Priority })
            .ToListAsync(ct);

        var meetingSummaries = await db.MeetingAiInsights.AsNoTracking()
            .Where(i => i.Meeting.ProjectId == projectId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(3)
            .Select(i => Clip(i.SummaryText, 350))
            .ToListAsync(ct);

        var systemPrompt = AiSuiteIdentity + " " +
            $"[AI_SUITE_DEFENSE_MOCK] Đóng vai Hội đồng Bảo vệ Đồ án Tốt nghiệp (gồm Chủ tịch CHAIR, Phản biện REVIEWER, Thư ký SECRETARY, Chuyên gia INDUSTRY). " +
            $"Sinh đúng {count} câu hỏi phản biện sắc bén, thực tế (trọng tâm vai trò: {roleFocus}) dựa trên hồ sơ đồ án thực tế để sinh viên luyện bảo vệ. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"headline\":string,\"readinessAssessment\":string,\"weakSpots\":string[],\"questions\":[{\"id\":string,\"committeeRole\":\"CHAIR\"|\"REVIEWER\"|\"SECRETARY\"|\"INDUSTRY\",\"difficulty\":\"EASY\"|\"MEDIUM\"|\"HARD\",\"category\":string,\"question\":string,\"whyAsked\":string,\"suggestedAnswerOutline\":string,\"keyPointsToMention\":string[],\"targetStudentName\":string|null}]}. " +
            "Chỉ điền targetStudentName bằng tên thật xuất hiện trong members hoặc null.";

        var payload = new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            projectDescription = Clip(project.Description, 1200),
            roleFocus,
            focusTopic = Clip(request.FocusTopic, 300),
            questionCount = count,
            members,
            deliverables,
            finalSubmissionItems = finalSub?.Items.Select(i => new { i.Title, i.VersionNumber, i.StatusAtSubmission }).ToArray() ?? [],
            recentReports,
            supervisorFeedbacks = feedbacks,
            openTasks = overdueTasks,
            recentMeetingAiSummaries = meetingSummaries
        };

        var raw = await CallAiAsync(systemPrompt, payload, ct);
        var allowedNames = members.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var status = ResolveGenerationStatus(true);
        DefenseMockPayload parsed;
        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            var questions = new List<DefenseMockQuestionDto>();
            if (r.TryGetProperty("questions", out var qArr) && qArr.ValueKind == JsonValueKind.Array)
            {
                var idx = 1;
                foreach (var q in qArr.EnumerateArray())
                {
                    var text = ReadString(q, "question", "", 800);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var rawTarget = ReadNullableString(q, "targetStudentName", 160);
                    var target = rawTarget is not null && allowedNames.Contains(rawTarget) ? rawTarget : null;
                    var role = ReadString(q, "committeeRole", "REVIEWER", 20).ToUpperInvariant();
                    if (role is not ("CHAIR" or "REVIEWER" or "SECRETARY" or "INDUSTRY")) role = "REVIEWER";
                    var diff = ReadString(q, "difficulty", "MEDIUM", 12).ToUpperInvariant();
                    if (diff is not ("EASY" or "MEDIUM" or "HARD")) diff = "MEDIUM";
                    questions.Add(new DefenseMockQuestionDto(
                        ReadString(q, "id", $"q{idx}", 24),
                        role, diff,
                        ReadString(q, "category", "Chuyên môn", 120),
                        text,
                        ReadString(q, "whyAsked", "Kiểm chứng độ hiểu sâu về đồ án.", 400),
                        ReadString(q, "suggestedAnswerOutline", "", 800),
                        ReadStringArray(q, "keyPointsToMention", 5, 200),
                        target));
                    idx++;
                    if (questions.Count >= 12) break;
                }
            }
            parsed = new DefenseMockPayload(
                ReadString(r, "headline", $"Bộ câu hỏi luyện bảo vệ — {project.Title}", 240),
                ReadString(r, "readinessAssessment", "Đã tạo bộ câu hỏi luyện bảo vệ dựa trên dữ liệu đồ án.", 1500),
                ReadStringArray(r, "weakSpots", 8, 300),
                questions);
        }
        else
        {
            status = "FALLBACK";
            parsed = new DefenseMockPayload(
                $"Luyện bảo vệ — {project.Title}",
                "Chưa thể kết nối AI tạo bộ câu hỏi lúc này. Vui lòng thử lại sau ít giây.",
                [], []);
        }

        var meta = await SaveSnapshotAsync(FeatureDefensePrep, "PROJECT", projectId, projectId, null, status, parsed.Headline, parsed, actor, actorName, ct);
        return new DefenseMockDto(projectId, parsed.Headline, parsed.ReadinessAssessment, parsed.WeakSpots, parsed.Questions, meta);
    }

    public async Task<DefenseAnswerEvalDto> EvaluateDefenseAnswerAsync(long projectId, DefenseAnswerEvalRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var question = (request.Question ?? string.Empty).Trim();
        var answer = (request.StudentAnswer ?? string.Empty).Trim();
        if (question.Length is < 5 or > 1000) throw Invalid("Câu hỏi phản biện không hợp lệ.");
        if (answer.Length is < 5 or > 4000) throw Invalid("Câu trả lời luyện tập phải từ 5 đến 4000 ký tự.");

        var (project, actor, actorName) = await RequireProjectAccessAsync(projectId, ct);

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_DEFENSE_EVAL] Đóng vai Giám khảo Hội đồng Bảo vệ chấm thử câu trả lời của sinh viên (có thể được nhập tay hoặc nhận diện từ giọng nói Web Speech). " +
            "Chấm công tâm trên thang 0..10, chỉ rõ điểm mạnh, ý còn thiếu, đưa ra mẫu câu trả lời thuyết phục hơn và 1 câu hỏi xoáy tiếp nối (followUpProbe). " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"score\":number,\"verdict\":\"STRONG\"|\"ADEQUATE\"|\"NEEDS_IMPROVEMENT\",\"feedback\":string,\"strengths\":string[],\"missingPoints\":string[],\"improvedAnswerSample\":string,\"followUpProbe\":string|null}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            projectDescription = Clip(project.Description, 800),
            committeeRole = request.CommitteeRole ?? "REVIEWER",
            question,
            suggestedOutline = Clip(request.SuggestedOutline, 600),
            studentAnswer = answer
        }, ct);

        var now = Now;
        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            var score = ReadDecimal(r, "score", 0m, 10m) ?? 7.0m;
            var verdict = ReadString(r, "verdict", score >= 7.5m ? "STRONG" : score >= 5.5m ? "ADEQUATE" : "NEEDS_IMPROVEMENT", 24).ToUpperInvariant();
            if (verdict is not ("STRONG" or "ADEQUATE" or "NEEDS_IMPROVEMENT")) verdict = "ADEQUATE";
            await RecordAuditAsync(actor, "AI_SUITE_DEFENSE_EVAL", "PROJECT", projectId, ct);
            return new DefenseAnswerEvalDto(
                projectId,
                score,
                verdict,
                ReadString(r, "feedback", "Đã chấm thử câu trả lời.", 1500),
                ReadStringArray(r, "strengths", 5, 240),
                ReadStringArray(r, "missingPoints", 5, 240),
                ReadString(r, "improvedAnswerSample", "", 1500),
                ReadNullableString(r, "followUpProbe", 400),
                BuildSnapshotMeta(null, "DEFENSE_EVAL", ResolveGenerationStatus(true), aiProvider.ModelVersion, now, actorName));
        }

        return new DefenseAnswerEvalDto(
            projectId, 0m, "NEEDS_IMPROVEMENT",
            "Chưa thể chấm câu trả lời lúc này. Vui lòng thử lại sau ít giây.",
            [], [], "", null,
            BuildSnapshotMeta(null, "DEFENSE_EVAL", "FALLBACK", aiProvider.ModelVersion, now, actorName));
    }

    public async Task<GateScriptDraftDto> GenerateGateScriptDraftAsync(long gateInstanceId, GateScriptDraftRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var actor = RequireActor();
        var gate = await db.ProjectGateInstances.AsNoTracking()
            .Include(g => g.Project)
            .Include(g => g.Requirements)
            .Include(g => g.Assignments)
            .SingleOrDefaultAsync(g => g.Id == gateInstanceId, ct);

        if (gate is null)
        {
            var resolvedGateId = await db.Set<GateReviewerAssignment>().AsNoTracking()
                .Where(a => a.Id == gateInstanceId)
                .Select(a => (long?)a.GateInstanceId)
                .FirstOrDefaultAsync(ct);
            if (resolvedGateId.HasValue)
            {
                gate = await db.ProjectGateInstances.AsNoTracking()
                    .Include(g => g.Project)
                    .Include(g => g.Requirements)
                    .Include(g => g.Assignments)
                    .SingleOrDefaultAsync(g => g.Id == resolvedGateId.Value, ct);
            }
        }

        if (gate is null) throw new NotFoundException("ProjectGateInstance", gateInstanceId);

        var isAssignedReviewer = gate.Assignments.Any(a => a.ReviewerId == actor);
        var isSupervisorOrStaff = await IsSupervisorOrStaffForProjectAsync(gate.ProjectId, actor, ct);
        if (!isAssignedReviewer && !isSupervisorOrStaff)
            throw new ForbiddenException("Chỉ thành viên hội đồng, GVHD hoặc Cán bộ Khoa mới có quyền tạo kịch bản hỏi đáp cổng.");

        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == actor).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? $"Người dùng #{actor}";
        var members = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == gate.Project.TeamId && m.LeftAt == null)
            .Select(m => new { m.UserId, Name = m.User.FullName, Role = m.IsLeader ? "Trưởng nhóm" : "Thành viên" })
            .ToListAsync(ct);

        var openTasks = await db.Tasks.AsNoTracking()
            .Where(t => t.Milestone.ProjectId == gate.ProjectId && t.Status != "DONE" && t.Status != "CANCELLED")
            .OrderBy(t => t.DueAt ?? DateTime.MaxValue)
            .Take(15)
            .Select(t => new { t.Title, t.Status, t.Priority, IsOverdue = t.DueAt.HasValue && t.DueAt.Value < Now })
            .ToListAsync(ct);

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_GATE_SCRIPT] Soạn kịch bản hỏi đáp phiên kiểm duyệt/bảo vệ (GateSessionScript) cho Hội đồng dựa trên hồ sơ cổng thực tế. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"executiveBrief\":string,\"strengthHighlights\":string[],\"riskWatchlist\":string[],\"agendaStages\":[{\"stage\":string,\"durationMinutes\":number,\"purpose\":string}],\"suggestedQuestions\":[{\"askerRole\":\"CHAIR\"|\"REVIEWER\"|\"SECRETARY\",\"question\":string,\"targetMemberName\":string|null,\"verificationGoal\":string}],\"remediationWatchlist\":string[],\"generalNotesDraft\":string}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            gateCode = gate.Code,
            gateTitle = gate.Title,
            gateObjective = gate.Objective,
            projectCode = gate.Project.Code,
            projectTitle = gate.Project.Title,
            focusNote = Clip(request.FocusNote, 400),
            members,
            requirements = gate.Requirements.Select(r => new { r.Code, r.Title, r.Required, r.VerificationStatus, r.ReviewerFeedback }).ToArray(),
            openTasks
        }, ct);

        var allowedMembers = members.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var status = ResolveGenerationStatus(true);
        GateScriptPayload parsed;
        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            var stages = new List<GateScriptAgendaDraftDto>();
            if (r.TryGetProperty("agendaStages", out var sArr) && sArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sArr.EnumerateArray().Take(8))
                {
                    var st = ReadString(s, "stage", "", 160);
                    if (string.IsNullOrWhiteSpace(st)) continue;
                    stages.Add(new GateScriptAgendaDraftDto(st, ReadInt(s, "durationMinutes", 10, 2, 120), ReadString(s, "purpose", "", 300)));
                }
            }
            var qs = new List<GateScriptQuestionDraftDto>();
            if (r.TryGetProperty("suggestedQuestions", out var qArr) && qArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in qArr.EnumerateArray().Take(10))
                {
                    var qt = ReadString(q, "question", "", 600);
                    if (string.IsNullOrWhiteSpace(qt)) continue;
                    var rawTarget = ReadNullableString(q, "targetMemberName", 160);
                    qs.Add(new GateScriptQuestionDraftDto(
                        ReadString(q, "askerRole", "REVIEWER", 24).ToUpperInvariant(),
                        qt,
                        rawTarget is not null && allowedMembers.Contains(rawTarget) ? rawTarget : null,
                        ReadString(q, "verificationGoal", "", 300)));
                }
            }
            parsed = new GateScriptPayload(
                ReadString(r, "executiveBrief", $"Tóm tắt phiên kiểm duyệt {gate.Code} — {gate.Project.Title}", 1500),
                ReadStringArray(r, "strengthHighlights", 6, 300),
                ReadStringArray(r, "riskWatchlist", 8, 300),
                stages, qs,
                ReadStringArray(r, "remediationWatchlist", 8, 300),
                ReadString(r, "generalNotesDraft", "", 2500));
        }
        else
        {
            status = "FALLBACK";
            parsed = new GateScriptPayload(
                $"Chưa thể tạo kịch bản AI cho cổng {gate.Code} lúc này.", [], [], [], [], [], "");
        }

        var meta = await SaveSnapshotAsync(FeatureGateScript, "GATE_INSTANCE", gateInstanceId, gate.ProjectId, null, status, $"Kịch bản {gate.Code} · {gate.Project.Code}", parsed, actor, actorName, ct);
        return new GateScriptDraftDto(
            gateInstanceId, gate.ProjectId, gate.Code,
            parsed.ExecutiveBrief, parsed.StrengthHighlights, parsed.RiskWatchlist,
            parsed.AgendaStages, parsed.SuggestedQuestions, parsed.RemediationWatchlist,
            parsed.GeneralNotesDraft, meta);
    }

    private sealed record DefenseMockPayload(
        string Headline,
        string ReadinessAssessment,
        IReadOnlyList<string> WeakSpots,
        IReadOnlyList<DefenseMockQuestionDto> Questions);

    private sealed record GateScriptPayload(
        string ExecutiveBrief,
        IReadOnlyList<string> StrengthHighlights,
        IReadOnlyList<string> RiskWatchlist,
        IReadOnlyList<GateScriptAgendaDraftDto> AgendaStages,
        IReadOnlyList<GateScriptQuestionDraftDto> SuggestedQuestions,
        IReadOnlyList<string> RemediationWatchlist,
        string GeneralNotesDraft);
}
