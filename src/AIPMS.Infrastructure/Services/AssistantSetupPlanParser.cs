using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Models;

namespace AIPMS.Infrastructure.Services;

internal static class AssistantSetupPlanParser
{
    private static readonly HashSet<string> ValidDeliverableTypes = new(StringComparer.OrdinalIgnoreCase)
        { "REPORT", "DOCUMENT", "SOURCE_CODE", "CODE", "DESIGN", "SLIDE", "OTHER" };
    private static readonly HashSet<string> ValidPriorities = new(StringComparer.Ordinal)
        { "LOW", "MEDIUM", "HIGH", "CRITICAL" };
    private static readonly HashSet<string> ValidDiagnosticCategories = new(StringComparer.Ordinal)
        { "DOCUMENT", "TASK", "REVIEW_GATE", "COLD_REPORT", "MILESTONE", "EVIDENCE" };
    private static readonly HashSet<string> ValidDiagnosticStatuses = new(StringComparer.Ordinal)
        { "DONE", "IN_PROGRESS", "NEEDS_REVISION", "BLOCKED", "OVERDUE", "MISSING", "PENDING_REVIEW" };

    public static AssistantProjectSetupPlanDto? ParseProjectSetup(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var summary = Text(value, "summary", 1000) ?? "Đề xuất thiết lập mốc, tiêu chí đầu ra và công việc theo ngữ cảnh đồ án.";

        var milestones = new List<AssistantSetupMilestoneDto>();
        if (value.TryGetProperty("milestones", out var msArr) && msArr.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in msArr.EnumerateArray().Take(8))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var title = Text(item, "title", 255);
                if (string.IsNullOrWhiteSpace(title)) continue;
                idx++;
                var tempKey = Text(item, "tempKey", 40) ?? $"M{idx}";
                long? existingId = item.TryGetProperty("existingMilestoneId", out var eid) && eid.ValueKind == JsonValueKind.Number && eid.TryGetInt64(out var parsedId) && parsedId > 0 ? parsedId : null;
                var start = Day(Text(item, "startDate", 10));
                var due = Day(Text(item, "dueDate", 10));
                if (start != null && due != null && string.CompareOrdinal(due, start) < 0) start = due = null;
                milestones.Add(new(tempKey, existingId, title, Text(item, "description", 2000), start, due));
            }
        }

        var deliverables = new List<AssistantSetupDeliverableDto>();
        if (value.TryGetProperty("deliverables", out var delArr) && delArr.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in delArr.EnumerateArray().Take(10))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var title = Text(item, "title", 255);
                if (string.IsNullOrWhiteSpace(title)) continue;
                idx++;
                var tempKey = Text(item, "tempKey", 40) ?? $"D{idx}";
                var rawType = Text(item, "deliverableType", 32)?.ToUpperInvariant() ?? "DOCUMENT";
                var deliverableType = ValidDeliverableTypes.Contains(rawType) ? rawType : "DOCUMENT";
                var scope = Text(item, "evidenceScope", 24)?.ToUpperInvariant();
                if (scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INTEGRATION")) scope = "COMMON";
                deliverables.Add(new(
                    tempKey, title, Text(item, "description", 2000), deliverableType,
                    Text(item, "milestoneRef", 40), scope, Text(item, "targetMajorName", 255),
                    Instant(Text(item, "dueAt", 64)), StringList(item, "acceptanceCriteria", 10, 500)));
            }
        }

        var tasks = new List<AssistantSetupTaskDto>();
        if (value.TryGetProperty("tasks", out var taskArr) && taskArr.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in taskArr.EnumerateArray().Take(15))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var title = Text(item, "title", 255);
                if (string.IsNullOrWhiteSpace(title)) continue;
                idx++;
                var tempKey = Text(item, "tempKey", 40) ?? $"T{idx}";
                var priority = Text(item, "priority", 16)?.ToUpperInvariant();
                if (priority is null || !ValidPriorities.Contains(priority)) priority = "MEDIUM";
                tasks.Add(new(
                    tempKey, title, Text(item, "description", 4000), priority,
                    Text(item, "milestoneRef", 40), Instant(Text(item, "dueAt", 64)),
                    StringList(item, "assigneeNames", 10, 255),
                    Text(item, "primaryMajorName", 255),
                    StringList(item, "acceptanceCriteria", 10, 500),
                    StringList(item, "dependsOnRefs", 5, 40)));
            }
        }

        if (milestones.Count == 0 && deliverables.Count == 0 && tasks.Count == 0) return null;
        var warnings = StringList(value, "warnings", 10, 400);
        return new(summary, milestones, deliverables, tasks, warnings);
    }

    public static AssistantRubricSetupPlanDto? ParseRubricSetup(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var name = Text(value, "name", 255);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var rawCode = Text(value, "code", 50) ?? "RUBRIC_AI_DRAFT";
        var code = Regex.Replace(rawCode.ToUpperInvariant(), @"[^A-Z0-9_\-]", "_");
        if (string.IsNullOrWhiteSpace(code)) code = "RUBRIC_AI_DRAFT";
        var mode = Text(value, "projectMode", 24)?.ToUpperInvariant();
        if (mode is not ("SINGLE_MAJOR" or "INTERDISCIPLINARY")) mode = "SINGLE_MAJOR";
        var stage = Text(value, "evaluationStage", 24)?.ToUpperInvariant();
        if (stage is not ("ON_GOING" or "COLD" or "DEFENSE")) stage = "DEFENSE";
        var scope = Text(value, "evaluationScope", 24)?.ToUpperInvariant();
        if (scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) scope = "COMMON";

        var criteria = new List<AssistantRubricCriterionDraftDto>();
        if (value.TryGetProperty("criteria", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in arr.EnumerateArray().Take(30))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var cName = Text(item, "name", 255);
                if (string.IsNullOrWhiteSpace(cName)) continue;
                idx++;
                var tempKey = Text(item, "tempKey", 40) ?? $"C{idx}";
                var parentKey = Text(item, "parentTempKey", 40);
                var weight = DecimalVal(item, "weightPercent") ?? 0m;
                if (weight <= 0 || weight > 100) continue;
                var maxScore = DecimalVal(item, "maxScore");
                if (maxScore is <= 0 or > 100) maxScore = 10m;
                var required = !item.TryGetProperty("isRequired", out var reqEl) || reqEl.ValueKind != JsonValueKind.False;
                var levels = new List<AssistantRubricLevelDraftDto>();
                if (item.TryGetProperty("levels", out var lvlArr) && lvlArr.ValueKind == JsonValueKind.Array)
                {
                    var order = 1;
                    foreach (var l in lvlArr.EnumerateArray().Take(6))
                    {
                        if (l.ValueKind != JsonValueKind.Object) continue;
                        var label = Text(l, "label", 80);
                        var desc = Text(l, "descriptor", 500);
                        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(desc)) continue;
                        var lOrder = l.TryGetProperty("levelOrder", out var ordEl) && ordEl.ValueKind == JsonValueKind.Number && ordEl.TryGetInt32(out var lo) && lo > 0 ? lo : order;
                        levels.Add(new(lOrder, label, desc, DecimalVal(l, "scoreFrom"), DecimalVal(l, "scoreTo")));
                        order++;
                    }
                }
                var isIntegration = item.TryGetProperty("isIntegration", out var intEl) && intEl.ValueKind == JsonValueKind.True;
                var targetScope = Text(item, "targetScope", 24)?.ToUpperInvariant();
                if (targetScope is not (null or "COMMON" or "MAJOR_SPECIFIC" or "INTEGRATION" or "INDIVIDUAL")) targetScope = null;
                criteria.Add(new(tempKey, parentKey, Text(item, "criterionCode", 32), cName, Text(item, "description", 1000),
                    Math.Round(weight, 2), maxScore, required, Text(item, "learningOutcome", 300),
                    Text(item, "evidenceGuidance", 500), levels, isIntegration, targetScope));
            }
        }
        if (criteria.Count == 0) return null;
        return new(code, name, Text(value, "description", 2000), mode, stage, scope,
            Text(value, "majorName", 255), criteria, StringList(value, "warnings", 10, 400));
    }

    public static AssistantReviewGateSetupPlanDto? ParseReviewGateSetup(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var name = Text(value, "name", 200);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var rawCode = Text(value, "code", 60) ?? "GATE_AI_DRAFT";
        var code = Regex.Replace(rawCode.ToUpperInvariant(), @"[^A-Z0-9_\-]", "_");
        if (string.IsNullOrWhiteSpace(code)) code = "GATE_AI_DRAFT";
        var mode = Text(value, "mode", 24)?.ToUpperInvariant();
        if (mode is not ("SINGLE_MAJOR" or "INTERDISCIPLINARY")) mode = "SINGLE_MAJOR";
        var majorCode = Text(value, "majorCode", 80) ?? Text(value, "major", 80);
        var summary = Text(value, "summary", 1000) ?? "Khung cổng thẩm định nháp do AI đề xuất. Kiểm tra ngày, trọng số (tổng 100%) và yêu cầu minh chứng trước khi lưu bản nháp.";

        var gates = new List<AssistantReviewGateDraftDto>();
        if (value.TryGetProperty("gates", out var gateArr) && gateArr.ValueKind == JsonValueKind.Array)
        {
            var seq = 0;
            foreach (var item in gateArr.EnumerateArray().Take(3))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var title = Text(item, "title", 200);
                if (string.IsNullOrWhiteSpace(title)) continue;
                seq++;
                var gCodeRaw = Text(item, "code", 32) ?? $"G{seq}";
                var gCode = Regex.Replace(gCodeRaw.ToUpperInvariant(), @"[^A-Z0-9_\-]", "_");
                if (string.IsNullOrWhiteSpace(gCode)) gCode = $"G{seq}";
                var gSeq = item.TryGetProperty("sequence", out var seqEl) && seqEl.ValueKind == JsonValueKind.Number && seqEl.TryGetInt32(out var s) && s is >= 1 and <= 3 ? s : seq;
                var weight = DecimalVal(item, "weight");
                if (weight is < 0 or > 100) weight = null;
                var startDate = Day(Text(item, "startDate", 10));
                var endDate = Day(Text(item, "endDate", 10));
                if (startDate != null && endDate != null && string.CompareOrdinal(endDate, startDate) < 0) endDate = startDate;
                var required = !item.TryGetProperty("required", out var reqEl) || reqEl.ValueKind != JsonValueKind.False;
                var blockNext = item.TryGetProperty("blockNextGate", out var bnEl) && bnEl.ValueKind == JsonValueKind.True;
                var blocksDefense = item.TryGetProperty("blocksDefense", out var bdEl) && bdEl.ValueKind == JsonValueKind.True;
                var reviewerPolicy = Text(item, "reviewerPolicy", 40)?.ToUpperInvariant();
                // QD-09: Independent gate reviewers only (SUPERVISOR is retired as gate reviewer)
                if (reviewerPolicy is not ("ASSIGNED_REVIEWER" or "LECTURER" or "COUNCIL" or "DEPARTMENT_STAFF")) reviewerPolicy = "COUNCIL";
                var reviewScope = Text(item, "reviewScope", 32)?.ToUpperInvariant();
                if (reviewScope is not ("COMMON" or "MAJOR_SPECIFIC" or "HYBRID")) reviewScope = mode == "INTERDISCIPLINARY" ? "HYBRID" : "COMMON";

                var reqs = new List<AssistantReviewGateRequirementDraftDto>();
                if (item.TryGetProperty("requirements", out var reqArr) && reqArr.ValueKind == JsonValueKind.Array)
                {
                    var rIdx = 0;
                    foreach (var r in reqArr.EnumerateArray().Take(15))
                    {
                        if (r.ValueKind != JsonValueKind.Object) continue;
                        var rTitle = Text(r, "title", 200);
                        if (string.IsNullOrWhiteSpace(rTitle)) continue;
                        rIdx++;
                        var rCodeRaw = Text(r, "code", 40) ?? $"{gCode}_REQ_{rIdx}";
                        var rCode = Regex.Replace(rCodeRaw.ToUpperInvariant(), @"[^A-Z0-9_\-]", "_");
                        if (string.IsNullOrWhiteSpace(rCode)) rCode = $"{gCode}_REQ_{rIdx}";
                        var rType = Text(r, "type", 24)?.ToUpperInvariant();
                        if (rType is not ("FILE" or "EXTERNAL_LINK" or "TEXT" or "ARTIFACT")) rType = "FILE";
                        var rScope = Text(r, "scope", 24)?.ToUpperInvariant();
                        if (rScope is not ("COMMON" or "MAJOR_SPECIFIC" or "INTEGRATION")) rScope = "COMMON";
                        var rVer = Text(r, "verification", 24)?.ToUpperInvariant();
                        if (rVer is not ("HUMAN" or "AUTO")) rVer = "HUMAN";
                        var rRequired = !r.TryGetProperty("required", out var rrEl) || rrEl.ValueKind != JsonValueKind.False;
                        var targetCard = Text(r, "targetCardinality", 24)?.ToUpperInvariant();
                        if (targetCard is not (null or "PROJECT" or "TEAM" or "EACH_STUDENT")) targetCard = null;
                        reqs.Add(new(rCode, rTitle, Text(r, "description", 600), rType, rScope,
                            rScope == "MAJOR_SPECIFIC" ? Text(r, "majorName", 160) : null, rVer, rRequired, targetCard));
                    }
                }
                if (reqs.Count == 0)
                {
                    reqs.Add(new($"{gCode}_DOC_1", $"Báo cáo & minh chứng cổng {gCode}", "Tài liệu minh chứng hoàn thành mục tiêu cổng.", "FILE", "COMMON", null, "HUMAN", true));
                }
                gates.Add(new(gCode, gSeq, title, Text(item, "objective", 1000) ?? $"Thẩm định kết quả tại cổng {gCode}.",
                    weight, startDate, endDate, required, blockNext, blocksDefense, reviewerPolicy, reviewScope, reqs));
            }
        }
        if (gates.Count == 0) return null;
        return new(code, name, mode, majorCode, summary, gates, StringList(value, "warnings", 10, 400));
    }

    public static AssistantTopicBatchPlanDto? ParseTopicBatchSetup(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var summary = Text(value, "summary", 1000) ?? "Danh sách đề tài nháp trích xuất bởi AI. Kiểm tra cảnh báo trùng 3 kỳ và chọn lọc trước khi lưu bản nháp.";
        var topics = new List<AssistantTopicDraftDto>();
        if (value.TryGetProperty("topics", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in arr.EnumerateArray().Take(25))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var title = Text(item, "title", 255);
                if (string.IsNullOrWhiteSpace(title)) continue;
                idx++;
                var tempKey = Text(item, "tempKey", 40) ?? $"TOP_{idx}";
                var rawCode = Text(item, "code", 40) ?? $"TOPIC_{idx:00}";
                var code = Regex.Replace(rawCode.ToUpperInvariant(), @"[^A-Z0-9_\-]", "_");
                if (code.Length < 2) code = $"TOPIC_{idx:00}";
                if (code.Length > 40) code = code[..40];
                var mode = Text(item, "projectMode", 24)?.ToUpperInvariant();
                if (mode is not ("SINGLE_MAJOR" or "INTERDISCIPLINARY")) mode = "SINGLE_MAJOR";
                long? primaryMajorId = item.TryGetProperty("primaryMajorId", out var pmEl) && pmEl.ValueKind == JsonValueKind.Number && pmEl.TryGetInt64(out var pmid) && pmid > 0 ? pmid : null;
                var primaryMajorName = Text(item, "primaryMajorName", 160);

                var reqs = new List<AssistantTopicRequirementDraftDto>();
                if (item.TryGetProperty("requirements", out var reqArr) && reqArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in reqArr.EnumerateArray().Take(6))
                    {
                        if (r.ValueKind != JsonValueKind.Object) continue;
                        long? mId = r.TryGetProperty("majorId", out var midEl) && midEl.ValueKind == JsonValueKind.Number && midEl.TryGetInt64(out var mid) && mid > 0 ? mid : null;
                        var mName = Text(r, "majorName", 160);
                        var minM = r.TryGetProperty("minMembers", out var minEl) && minEl.ValueKind == JsonValueKind.Number && minEl.TryGetInt32(out var mn) && mn >= 1 ? Math.Min(mn, 12) : 1;
                        var maxM = r.TryGetProperty("maxMembers", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number && maxEl.TryGetInt32(out var mx) && mx >= minM ? Math.Min(mx, 12) : Math.Max(minM, 5);
                        var resp = Text(r, "responsibility", 500) ?? "Thực hiện phần chuyên môn chính của đề tài.";
                        reqs.Add(new(mId, mName, minM, maxM, resp));
                    }
                }

                var trackCode = Text(item, "trackCode", 24)?.ToUpperInvariant();
                if (trackCode is not (null or "IT_ONLY" or "MKT_ONLY" or "IT_MKT")) trackCode = null;
                var maxTeams = item.TryGetProperty("maxTeams", out var mtEl) && mtEl.ValueKind == JsonValueKind.Number && mtEl.TryGetInt32(out var mt) && mt >= 1
                    ? Math.Min(mt, 10) : 1;

                topics.Add(new(
                    tempKey, code, title,
                    Text(item, "description", 2000),
                    Text(item, "problemStatement", 2000),
                    Text(item, "objectives", 2000),
                    Text(item, "expectedOutput", 2000),
                    Text(item, "domain", 120),
                    StringList(item, "technologies", 12, 80),
                    StringList(item, "keywords", 12, 80),
                    mode, primaryMajorId, primaryMajorName, reqs,
                    TrackCode: trackCode,
                    MaxTeams: maxTeams));
            }
        }
        if (topics.Count == 0) return null;
        return new(summary, topics, StringList(value, "warnings", 10, 400));
    }

    public static AssistantColdTemplateSetupPlanDto? ParseColdTemplateSetup(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var name = Text(value, "name", 200);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var scope = Text(value, "scope", 24)?.ToUpperInvariant();
        if (scope is not ("COMMON" or "MAJOR_SPECIFIC")) scope = "MAJOR_SPECIFIC";
        var majorName = scope == "MAJOR_SPECIFIC" ? Text(value, "majorName", 160) : null;
        var summary = Text(value, "summary", 1000) ?? "Danh mục báo cáo chấm nguội đề xuất bởi AI. Kiểm tra mã báo cáo và phạm vi trước khi lưu phiên bản mới.";

        var reports = new List<AssistantColdReportItemDraftDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (value.TryGetProperty("reports", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in arr.EnumerateArray().Take(30))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var rName = Text(item, "name", 200);
                if (string.IsNullOrWhiteSpace(rName)) continue;
                idx++;
                var rawCode = Text(item, "code", 80) ?? $"REPORT_{idx}";
                var code = Regex.Replace(rawCode.ToUpperInvariant(), @"[^A-Z0-9_]", "_").Trim('_');
                if (string.IsNullOrWhiteSpace(code)) code = $"REPORT_{idx}";
                if (code.Length > 80) code = code[..80];
                if (!seen.Add(code))
                {
                    code = $"{code[..Math.Min(code.Length, 72)]}_{idx}";
                    seen.Add(code);
                }
                var required = !item.TryGetProperty("required", out var reqEl) || reqEl.ValueKind != JsonValueKind.False;
                reports.Add(new(code, rName, required));
            }
        }
        if (reports.Count == 0) return null;
        if (!reports.Any(r => r.Required))
        {
            reports[0] = reports[0] with { Required = true };
        }
        return new(name, scope, majorName, summary, reports, StringList(value, "warnings", 10, 400));
    }

    public static AssistantMilestoneTemplateSetupPlanDto? ParseMilestoneTemplateSetup(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var name = Text(value, "name", 200);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var summary = Text(value, "summary", 1000) ?? "Bộ mốc mẫu nháp đề xuất bởi AI. Kiểm tra ngày lệch (offset) trước khi lưu bản nháp.";
        var items = new List<AssistantMilestoneTemplateItemDraftDto>();
        if (value.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in arr.EnumerateArray().Take(20))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var title = Text(item, "title", 200);
                if (string.IsNullOrWhiteSpace(title)) continue;
                int? startOff = item.TryGetProperty("startOffsetDays", out var sEl) && sEl.ValueKind == JsonValueKind.Number && sEl.TryGetInt32(out var so) && so >= 0 ? Math.Min(so, 365) : null;
                int? dueOff = item.TryGetProperty("dueOffsetDays", out var dEl) && dEl.ValueKind == JsonValueKind.Number && dEl.TryGetInt32(out var doff) && doff >= 0 ? Math.Min(doff, 365) : null;
                if (startOff.HasValue && dueOff.HasValue && dueOff < startOff) dueOff = startOff;
                items.Add(new(title, Text(item, "description", 1000), startOff, dueOff, idx));
                idx++;
            }
        }
        if (items.Count == 0) return null;
        return new(name, Text(value, "description", 1000), summary, items, StringList(value, "warnings", 10, 400));
    }

    public static AssistantStructuredReportDto? ParseStructuredReport(
        JsonElement root,
        ProjectBoundedContext? context,
        IReadOnlyList<EvidenceReferenceDto> selectedProjectEvidence,
        IReadOnlyDictionary<string, EvidenceReferenceDto> evidenceByCitationId)
    {
        var coverage = BuildCoverage(context);
        if (root.TryGetProperty("structuredReport", out var sr) && sr.ValueKind == JsonValueKind.Object
            && sr.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            var title = Text(sr, "summaryTitle", 200) ?? "Tổng hợp theo ngữ cảnh đồ án";
            var parsed = new List<AssistantDiagnosticItemDto>();
            foreach (var item in itemsEl.EnumerateArray().Take(20))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var category = Text(item, "category", 24)?.ToUpperInvariant() ?? "TASK";
                if (!ValidDiagnosticCategories.Contains(category)) category = "TASK";
                var itemTitle = Text(item, "title", 255);
                var location = Text(item, "location", 255) ?? "Trong đồ án";
                var status = Text(item, "status", 24)?.ToUpperInvariant() ?? "IN_PROGRESS";
                if (!ValidDiagnosticStatuses.Contains(status)) status = "IN_PROGRESS";
                var progress = Text(item, "progressText", 200) ?? status;
                if (string.IsNullOrWhiteSpace(itemTitle)) continue;
                var citationId = Text(item, "citationId", 32);
                string? target = null;
                long? resourceId = null;
                if (!string.IsNullOrWhiteSpace(citationId) && evidenceByCitationId.TryGetValue(citationId, out var ev))
                {
                    (target, resourceId) = ResolveTarget(ev);
                }
                parsed.Add(new(category, itemTitle, location, status, progress,
                    Text(item, "issueDetail", 400), Text(item, "owner", 180), Text(item, "dueAt", 64),
                    target, resourceId, citationId));
            }
            if (parsed.Count > 0 && coverage is not null)
                return new(title, coverage, parsed);
        }

        if (context is not null && selectedProjectEvidence.Count > 0)
            return BuildDeterministicStructuredReport(context, selectedProjectEvidence);
        return null;
    }

    public static AssistantStructuredReportDto? BuildDeterministicStructuredReport(
        ProjectBoundedContext context,
        IReadOnlyList<EvidenceReferenceDto>? filterEvidence = null)
    {
        ProjectEvidencePayload? payload = null;
        try { payload = JsonSerializer.Deserialize<ProjectEvidencePayload>(context.FormattedEvidenceText); }
        catch (JsonException) { }
        if (payload is null) return null;

        var coverage = BuildCoverageFromPayload(payload);
        var items = new List<AssistantDiagnosticItemDto>();
        var filterIds = filterEvidence is { Count: > 0 }
            ? filterEvidence.Select(e => e.SourceId).ToHashSet(StringComparer.Ordinal)
            : null;

        // 1. Deliverables
        foreach (var d in (payload.Deliverables ?? []).Where(x => filterIds is null || filterIds.Contains(x.Id)).Take(8))
        {
            var status = d.Status is "ACCEPTED" or "APPROVED" ? "DONE"
                : d.Status == "REVISION_REQUIRED" || d.LatestVersionStatus == "REVISION_REQUIRED" ? "NEEDS_REVISION"
                : d.IsOverdue ? "OVERDUE"
                : d.LatestVersionNumber == 0 ? "MISSING"
                : "PENDING_REVIEW";
            var progress = d.LatestVersionNumber > 0
                ? $"Phiên bản v{d.LatestVersionNumber} ({d.FileNames.Count} tệp)"
                : "Chưa nộp tệp nào";
            var issue = !string.IsNullOrWhiteSpace(d.LatestFeedback)
                ? $"Phản hồi GVHD: {d.LatestFeedback}"
                : d.IsOverdue ? "Hạng mục bàn giao đã quá hạn." : d.LatestVersionNumber == 0 ? "Chưa có phiên bản nộp." : null;
            items.Add(new("DOCUMENT", d.Title,
                string.IsNullOrWhiteSpace(d.MilestoneTitle) ? "Hạng mục cần nộp" : $"Mốc: {d.MilestoneTitle} · Hạng mục nộp",
                status, progress, issue, d.TargetMajorName ?? "Cả nhóm", d.DueAt, "deliverables", d.DeliverableId, d.Id));
        }

        // 2. Cold Reports
        foreach (var c in (payload.ColdReports ?? []).Where(x => filterIds is null || filterIds.Contains(x.Id)).Take(6))
        {
            var status = c.SubmissionStatus is "SUBMITTED" or "LOCKED" ? "DONE"
                : c.SubmissionStatus == "DRAFT" ? "IN_PROGRESS" : "MISSING";
            var progress = c.SubmissionStatus switch
            {
                "LOCKED" => $"Đã khóa · v{c.LatestVersion} ({c.FileCount} tệp)",
                "SUBMITTED" => $"Đã nộp · v{c.LatestVersion} ({c.FileCount} tệp)",
                "DRAFT" => $"Bản nháp · v{c.LatestVersion} ({c.FileCount} tệp)",
                _ => "Chưa tạo bản nháp"
            };
            items.Add(new("COLD_REPORT", c.Name,
                $"Chấm nguội · {c.ReportType}{(c.MajorName != null ? " · " + c.MajorName : "")}",
                status, progress,
                status == "MISSING" && c.Required ? "Hạng mục chấm nguội bắt buộc chưa được nộp." : c.Note,
                c.MajorName ?? "Nhóm đồ án", c.Deadline, "cold-reports", null, c.Id));
        }

        // 3. Review Gates (issues & pending requirements)
        foreach (var g in (payload.ReviewGates ?? []).Where(x => filterIds is null || filterIds.Contains(x.Id)).Take(4))
        {
            var rejected = g.Requirements.Where(r => r.VerificationStatus is "REJECTED" or "REVISION_REQUIRED").ToArray();
            var missing = g.Requirements.Where(r => string.IsNullOrWhiteSpace(r.LatestArtifactName) && r.Required).ToArray();
            var verified = g.Requirements.Count(r => r.VerificationStatus is "VERIFIED" or "APPROVED" or "ACCEPTED");
            var status = g.Status is "PASSED" or "APPROVED" ? "DONE"
                : rejected.Length > 0 ? "NEEDS_REVISION"
                : missing.Length > 0 ? "MISSING"
                : "PENDING_REVIEW";
            var issue = rejected.Length > 0
                ? string.Join("; ", rejected.Take(2).Select(r => $"{r.Title}: {r.ReviewerFeedback ?? "Cần sửa theo phản hồi thẩm định"}"))
                : missing.Length > 0
                ? $"Còn thiếu {missing.Length} tài liệu bắt buộc: {string.Join(", ", missing.Take(3).Select(r => r.Title))}"
                : null;
            items.Add(new("REVIEW_GATE", $"{g.GateCode}: {g.Title}",
                $"Tiến trình Review Gates · {g.Status}",
                status, $"Đạt {verified}/{g.Requirements.Count} yêu cầu",
                issue, "Nhóm & Hội đồng thẩm định", g.WindowEnd, "timeline", null, g.Id));
        }

        // 4. Tasks (prioritize blocked/overdue/impact/open)
        var orderedTasks = payload.Tasks
            .Where(t => filterIds is null || filterIds.Contains(t.Id))
            .OrderByDescending(t => t.IsBlocked)
            .ThenByDescending(t => t.IsOverdue)
            .ThenByDescending(t => t.PendingImpactCount > 0)
            .ThenBy(t => t.Status is "DONE" or "COMPLETED")
            .Take(10);
        foreach (var t in orderedTasks)
        {
            var status = t.Status is "DONE" or "COMPLETED" ? "DONE"
                : t.IsBlocked ? "BLOCKED"
                : t.IsOverdue ? "OVERDUE"
                : t.PendingImpactCount > 0 ? "NEEDS_REVISION"
                : t.Status == "IN_REVIEW" ? "PENDING_REVIEW"
                : "IN_PROGRESS";
            var issueParts = new List<string>();
            if (t.BlockingTaskTitles is { Count: > 0 }) issueParts.Add($"Bị chặn bởi: {string.Join(", ", t.BlockingTaskTitles)}");
            if (!string.IsNullOrWhiteSpace(t.ImpactSummary)) issueParts.Add(t.ImpactSummary);
            if (t.IsOverdue) issueParts.Add("Đã quá hạn hoàn thành.");
            if (t.IsBlocked && issueParts.Count == 0) issueParts.Add("Đang ở trạng thái vướng mắc.");
            if (!string.IsNullOrWhiteSpace(t.LatestComment)) issueParts.Add($"Bình luận: {t.LatestComment}");
            var progress = t.CriteriaTotal > 0
                ? $"Tiêu chí {t.CriteriaCompleted}/{t.CriteriaTotal} · {t.FileCount} tệp"
                : $"{t.Status} · {t.FileCount} tệp minh chứng";
            items.Add(new("TASK", t.Title,
                string.IsNullOrWhiteSpace(t.MilestoneTitle) ? "Bảng công việc" : $"Mốc: {t.MilestoneTitle}",
                status, progress,
                issueParts.Count > 0 ? string.Join(" ", issueParts) : null,
                t.AssigneeNames is { Count: > 0 } ? string.Join(", ", t.AssigneeNames) : "Chưa phân công",
                t.DueAt, "tasks", t.TaskId, t.Id));
        }

        // 5. Evidence Ledger issues
        foreach (var ev in (payload.EvidenceLedger ?? []).Where(e => e.VerificationStatus is "REJECTED" or "PENDING").Take(4))
        {
            if (filterIds is not null && !filterIds.Contains(ev.Id)) continue;
            items.Add(new("EVIDENCE", $"Minh chứng {ev.SourceType} #{ev.SourceId}",
                $"Sổ minh chứng{(ev.MajorName != null ? " · " + ev.MajorName : "")}",
                ev.VerificationStatus == "REJECTED" ? "NEEDS_REVISION" : "PENDING_REVIEW",
                ev.VerificationStatus == "REJECTED" ? "Bị từ chối xác minh" : "Đang chờ xác minh",
                ev.Notes, ev.MajorName ?? "GVHD / Mentor", null, "files", null, ev.Id));
        }

        if (items.Count == 0) return null;
        return new("Tình trạng tài liệu & công việc trong đồ án", coverage, items);
    }

    public static AssistantContextCoverageDto? BuildCoverage(ProjectBoundedContext? context)
    {
        if (context is null) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<ProjectEvidencePayload>(context.FormattedEvidenceText);
            return payload is null ? null : BuildCoverageFromPayload(payload);
        }
        catch (JsonException) { return null; }
    }

    private static AssistantContextCoverageDto BuildCoverageFromPayload(ProjectEvidencePayload payload) =>
        new(payload.Overview?.Title, payload.ProjectStatus, payload.RetrievedTasks, payload.TotalTasks,
            payload.Deliverables?.Count ?? 0, payload.ColdReports?.Count ?? 0, payload.ReviewGates?.Count ?? 0);

    public static (string? Target, long? ResourceId) ResolveTarget(EvidenceReferenceDto ev)
    {
        var url = ev.ReferenceUrl ?? "";
        var m = Regex.Match(url, @"^/api/v1/(tasks|milestones|meetings|progress-reports|deliverables)/(\d+)$");
        if (m.Success)
        {
            var target = m.Groups[1].Value switch
            {
                "progress-reports" => "reports",
                var x => x
            };
            return (target, long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }
        if (url.Contains("/cold/", StringComparison.Ordinal)) return ("cold-reports", null);
        if (url.Contains("/review-gates", StringComparison.Ordinal)) return ("timeline", null);
        if (url.Contains("/evidence", StringComparison.Ordinal)) return ("files", null);
        return (ev.SourceType switch
        {
            "DELIVERABLE" => "deliverables",
            "COLD_REPORT" => "cold-reports",
            "REVIEW_GATE" => "timeline",
            "PROJECT_EVIDENCE" => "files",
            "TASK" => "tasks",
            "MILESTONE" => "milestones",
            "PROGRESS_REPORT" => "reports",
            "MEETING" => "meetings",
            _ => null
        }, null);
    }

    private static string? Text(JsonElement value, string key, int max) =>
        value.TryGetProperty(key, out var t) && t.ValueKind == JsonValueKind.String
        && t.GetString()?.Trim() is { Length: > 0 } text
            ? (text.Length <= max ? text : text[..max])
            : null;

    private static decimal? DecimalVal(JsonElement value, string key)
    {
        if (!value.TryGetProperty(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;
        if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static string? Day(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static DateTimeOffset? Instant(string? text) => text is not null
        && Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})$")
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToUniversalTime() : null;

    private static string[] StringList(JsonElement value, string key, int maxItems, int maxLength)
    {
        if (!value.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        return arr.EnumerateArray().Take(maxItems)
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Length <= maxLength ? s : s[..maxLength])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
