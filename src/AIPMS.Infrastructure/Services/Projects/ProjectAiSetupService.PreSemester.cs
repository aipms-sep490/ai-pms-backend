using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.MilestoneTemplates.Commands;
using AIPMS.Application.Features.MilestoneTemplates.DTOs;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.Topics.Commands;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ProjectAiSetupService
{
    public async Task<ApplyReviewGateSetupResultDto> ApplyReviewGateSetupAsync(
        ApplyReviewGateSetupRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId) throw new UnauthorizedException();
        if (request is null || request.IdempotencyKey == Guid.Empty)
            throw new ArgumentException("Thiếu mã xác thực chống trùng (idempotencyKey).");
        if (request.Gates is null || request.Gates.Count == 0)
            throw new ArgumentException("Khung cổng thẩm định cần tối thiểu một cổng.");
        if (request.Gates.Count > 10)
            throw new ArgumentException("Tối đa 10 cổng thẩm định cho mỗi mẫu.");
        if (reviewGates is null)
            throw new ConflictException("Dịch vụ cổng thẩm định chưa sẵn sàng.");

        var replay = await db.AiSetupApplications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActorId == actorId && x.IdempotencyKey == request.IdempotencyKey && x.ScopeType == "REVIEW_GATE", ct);
        if (replay is not null)
        {
            var prev = JsonSerializer.Deserialize<ApplyReviewGateSetupResultDto>(replay.ResultJson);
            if (prev is not null) return prev with { Replay = true };
        }

        var actor = await RequireDepartmentStaffActorAsync(actorId, ct);
        var orgMajors = await db.Majors.AsNoTracking()
            .Where(m => m.IsActive && m.Department.OrganizationId == actor.OrganizationId)
            .Select(m => new { m.Id, m.Code, m.Name, m.DepartmentId })
            .ToListAsync(ct);

        var warnings = new List<string>();
        var mode = request.Mode?.Trim().ToUpperInvariant() is "INTERDISCIPLINARY" ? "INTERDISCIPLINARY" : "SINGLE_MAJOR";
        var code = Regex.Replace((request.Code ?? "GATE_AI_DRAFT").Trim().ToUpperInvariant(), @"[^A-Z0-9_\-]", "_");
        if (string.IsNullOrWhiteSpace(code)) code = "GATE_AI_DRAFT";
        if (code.Length > 60) code = code[..60];
        var name = string.IsNullOrWhiteSpace(request.Name) ? "Mẫu cổng thẩm định (Nháp AI)" : request.Name.Trim();
        if (name.Length > 200) name = name[..200];

        var hasAnyWeight = request.Gates.Any(g => g.Weight.HasValue);
        var gatesInput = new List<ReviewGateDefinitionInput>(request.Gates.Count);
        var seenGateCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < request.Gates.Count; i++)
        {
            var g = request.Gates[i];
            var gCode = Regex.Replace((g.Code ?? $"G{i + 1}").Trim().ToUpperInvariant(), @"[^A-Z0-9_\-]", "_");
            if (string.IsNullOrWhiteSpace(gCode) || !seenGateCodes.Add(gCode)) gCode = $"G{i + 1}";
            var gTitle = string.IsNullOrWhiteSpace(g.Title) ? $"Cổng {gCode}" : g.Title.Trim();
            DateTime? startDt = ParseDay(g.StartDate) is DateOnly sd ? DateTime.SpecifyKind(sd.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) : null;
            DateTime? endDt = ParseDay(g.EndDate) is DateOnly ed ? DateTime.SpecifyKind(ed.ToDateTime(new TimeOnly(23, 59, 59)), DateTimeKind.Utc) : null;
            if (startDt.HasValue && endDt.HasValue && endDt < startDt)
            {
                endDt = startDt.Value.AddDays(1);
                warnings.Add($"Cổng {gCode}: ngày kết thúc sớm hơn ngày bắt đầu nên đã tự điều chỉnh.");
            }

            var reqInputs = new List<ReviewGateRequirementInput>();
            var rIndex = 0;
            foreach (var r in g.Requirements ?? [])
            {
                if (string.IsNullOrWhiteSpace(r.Title)) continue;
                rIndex++;
                var rCode = Regex.Replace((r.Code ?? $"{gCode}_REQ_{rIndex}").Trim().ToUpperInvariant(), @"[^A-Z0-9_\-]", "_");
                if (string.IsNullOrWhiteSpace(rCode)) rCode = $"{gCode}_REQ_{rIndex}";
                var rType = r.Type?.Trim().ToUpperInvariant();
                if (rType is not ("FILE" or "EXTERNAL_LINK" or "TEXT" or "ARTIFACT")) rType = "FILE";
                var rScope = r.Scope?.Trim().ToUpperInvariant() == "MAJOR_SPECIFIC" ? "MAJOR_SPECIFIC" : "COMMON";
                long? majorId = null;
                if (rScope == "MAJOR_SPECIFIC")
                {
                    var match = !string.IsNullOrWhiteSpace(r.MajorName)
                        ? orgMajors.Where(m => Normalize(m.Name) == Normalize(r.MajorName) || Normalize(m.Code) == Normalize(r.MajorName)).ToArray()
                        : [];
                    if (match.Length == 1)
                    {
                        majorId = match[0].Id;
                    }
                    else
                    {
                        rScope = "COMMON";
                        warnings.Add($"Cổng {gCode} · Yêu cầu \"{r.Title.Trim()}\": không khớp duy nhất chuyên ngành \"{r.MajorName}\", đã chuyển sang phạm vi chung (COMMON).");
                    }
                }
                var rVer = r.Verification?.Trim().ToUpperInvariant() == "AUTO" ? "AUTO" : "HUMAN";
                reqInputs.Add(new ReviewGateRequirementInput(rCode, r.Title.Trim(), r.Description?.Trim(), rType, rScope, majorId, null, rVer, r.Required));
            }
            if (reqInputs.Count == 0)
            {
                reqInputs.Add(new ReviewGateRequirementInput($"{gCode}_DOC_1", $"Minh chứng cổng {gCode}", "Tài liệu minh chứng hoàn thành cổng.", "FILE", "COMMON", null, null, "HUMAN", true));
            }

            var reviewerPolicy = g.ReviewerPolicy?.Trim().ToUpperInvariant();
            if (reviewerPolicy is not ("SUPERVISOR" or "LECTURER" or "COUNCIL" or "DEPARTMENT_STAFF")) reviewerPolicy = "COUNCIL";
            var reviewScope = g.ReviewScope?.Trim().ToUpperInvariant();
            if (reviewScope is not ("COMMON" or "MAJOR_SPECIFIC" or "HYBRID")) reviewScope = mode == "INTERDISCIPLINARY" ? "HYBRID" : "COMMON";

            gatesInput.Add(new ReviewGateDefinitionInput(
                gCode, i + 1, gTitle, g.Objective?.Trim() ?? "",
                g.Required, g.BlockNextGate, g.BlocksDefense,
                startDt, endDt, reviewerPolicy, reviewScope, reqInputs,
                hasAnyWeight ? (g.Weight ?? 0m) : null));
        }

        if (hasAnyWeight)
        {
            var sum = gatesInput.Sum(x => x.Weight ?? 0m);
            if (sum != 100m)
                throw new ArgumentException($"Tổng trọng số các cổng thẩm định phải bằng 100% (hiện tại là {sum:0.##}%).");
        }

        var saveReq = new SaveReviewGateTemplateRequest(
            request.ProjectPeriodId, code, name, mode, request.Major?.Trim() ?? "", "1.0", null, gatesInput);
        var created = await reviewGates.SaveTemplate(saveReq, null, ct);
        var templateId = long.TryParse(created.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) ? parsedId : 0L;

        var result = new ApplyReviewGateSetupResultDto(
            templateId, created.Code, created.Name, created.Status, created.GatesCount, false,
            warnings.Count > 0 ? warnings : null);

        db.AiSetupApplications.Add(new AiSetupApplication
        {
            ActorId = actorId,
            IdempotencyKey = request.IdempotencyKey,
            ScopeType = "REVIEW_GATE",
            ProjectId = null,
            RunId = request.RunId,
            ResultJson = JsonSerializer.Serialize(result),
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(actorId, "AI_REVIEW_GATE_SETUP_APPLIED", "REVIEW_GATE_TEMPLATE", templateId,
            new Dictionary<string, object?> { ["runId"] = request.RunId, ["code"] = created.Code, ["gatesCount"] = created.GatesCount }), ct);
        return result;
    }

    public async Task<ApplyTopicBatchSetupResultDto> ApplyTopicBatchSetupAsync(
        ApplyTopicBatchSetupRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId) throw new UnauthorizedException();
        if (request is null || request.IdempotencyKey == Guid.Empty)
            throw new ArgumentException("Thiếu mã xác thực chống trùng (idempotencyKey).");
        if (request.ProjectPeriodId <= 0)
            throw new ArgumentException("Cần chọn kỳ đăng ký đồ án để lưu danh sách đề tài nháp.");
        if (request.Topics is null || request.Topics.Count == 0)
            throw new ArgumentException("Cần chọn ít nhất một đề tài để lưu bản nháp.");
        if (request.Topics.Count > 25)
            throw new ArgumentException("Tối đa 25 đề tài trong mỗi lượt tạo hàng loạt.");

        var replay = await db.AiSetupApplications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActorId == actorId && x.IdempotencyKey == request.IdempotencyKey && x.ScopeType == "TOPIC_BATCH", ct);
        if (replay is not null)
        {
            var prev = JsonSerializer.Deserialize<ApplyTopicBatchSetupResultDto>(replay.ResultJson);
            if (prev is not null) return prev with { Replay = true };
        }

        var actor = await RequireDepartmentStaffActorAsync(actorId, ct);
        var leadDeptId = request.LeadDepartmentId ?? actor.DepartmentId;
        if (leadDeptId != actor.DepartmentId)
            throw new ForbiddenException("Bạn chỉ có thể tạo đề tài hàng loạt trong phạm vi bộ môn được phân công.");

        var orgMajors = await db.Majors.AsNoTracking()
            .Where(m => m.IsActive && m.Department.OrganizationId == actor.OrganizationId)
            .Select(m => new { m.Id, m.Code, m.Name, m.DepartmentId })
            .ToListAsync(ct);
        var deptMajors = orgMajors.Where(m => m.DepartmentId == leadDeptId).ToList();
        var defaultDeptMajor = deptMajors.FirstOrDefault()
            ?? throw new ArgumentException("Bộ môn hiện tại chưa có chuyên ngành hoạt động để gắn vào đề tài.");

        long? ResolveMajorId(long? explicitId, string? nameOrCode, bool preferDept)
        {
            if (explicitId.HasValue && orgMajors.Any(m => m.Id == explicitId.Value)) return explicitId.Value;
            if (!string.IsNullOrWhiteSpace(nameOrCode))
            {
                var pool = preferDept ? deptMajors : orgMajors;
                var hit = pool.FirstOrDefault(m => Normalize(m.Name) == Normalize(nameOrCode) || Normalize(m.Code) == Normalize(nameOrCode))
                    ?? orgMajors.FirstOrDefault(m => Normalize(m.Name) == Normalize(nameOrCode) || Normalize(m.Code) == Normalize(nameOrCode));
                if (hit is not null) return hit.Id;
            }
            return null;
        }

        var created = new List<TopicBatchCreatedItemDto>();
        var skipped = new List<TopicBatchSkippedItemDto>();
        var warnings = new List<string>();

        for (var i = 0; i < request.Topics.Count; i++)
        {
            var item = request.Topics[i];
            var tempKey = string.IsNullOrWhiteSpace(item.TempKey) ? $"TOP_{i + 1}" : item.TempKey.Trim();
            var rawCode = string.IsNullOrWhiteSpace(item.Code) ? $"AI_TOPIC_{i + 1:00}" : item.Code.Trim();
            var code = Regex.Replace(rawCode.ToUpperInvariant(), @"[^A-Z0-9_\-]", "_");
            if (code.Length > 40) code = code[..40];
            var title = item.Title?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(title) || title.Length > 255)
            {
                skipped.Add(new(tempKey, code, title, "Tên đề tài trống hoặc vượt quá 255 ký tự."));
                continue;
            }

            var mode = item.ProjectMode?.Trim().ToUpperInvariant() == "INTERDISCIPLINARY" ? "INTERDISCIPLINARY" : "SINGLE_MAJOR";
            var reqList = new List<TopicMajorRequirementRequest>();
            var usedMajorIds = new HashSet<long>();
            foreach (var r in item.Requirements ?? [])
            {
                var mId = ResolveMajorId(r.MajorId, r.MajorName, mode == "SINGLE_MAJOR");
                if (mId.HasValue && usedMajorIds.Add(mId.Value))
                {
                    var minM = Math.Clamp(r.MinMembers, 1, 12);
                    var maxM = Math.Clamp(r.MaxMembers, minM, 12);
                    reqList.Add(new(mId.Value, minM, maxM, string.IsNullOrWhiteSpace(r.Responsibility) ? "Thực hiện phần chuyên môn chính." : r.Responsibility.Trim()));
                }
            }

            long? primaryMajorId = ResolveMajorId(item.PrimaryMajorId, item.PrimaryMajorName, true);
            if (mode == "SINGLE_MAJOR")
            {
                var singleMajorId = primaryMajorId ?? reqList.FirstOrDefault(r => deptMajors.Any(d => d.Id == r.MajorId))?.MajorId ?? defaultDeptMajor.Id;
                primaryMajorId = singleMajorId;
                if (reqList.Count == 0 || reqList.All(r => r.MajorId != singleMajorId))
                {
                    reqList = [new(singleMajorId, 3, 5, "Phân tích, thiết kế, triển khai và hoàn thiện toàn bộ sản phẩm đồ án.")];
                }
                else
                {
                    reqList = [reqList.First(r => r.MajorId == singleMajorId)];
                }
            }
            else
            {
                if (!reqList.Any(r => deptMajors.Any(d => d.Id == r.MajorId)))
                {
                    var leadMajorId = primaryMajorId ?? defaultDeptMajor.Id;
                    if (usedMajorIds.Add(leadMajorId))
                        reqList.Insert(0, new(leadMajorId, 2, 4, "Phụ trách chuyên môn chủ trì của bộ môn."));
                }
                if (reqList.Count < 2)
                {
                    var otherMajor = orgMajors.FirstOrDefault(m => !usedMajorIds.Contains(m.Id));
                    if (otherMajor is not null)
                    {
                        reqList.Add(new(otherMajor.Id, 1, 3, "Phụ trách phần phối hợp chuyên ngành liên kết."));
                        warnings.Add($"Đề tài \"{title}\": chế độ liên ngành chưa đủ 2 chuyên ngành nên tạm gắn thêm ngành {otherMajor.Name}.");
                    }
                    else
                    {
                        mode = "SINGLE_MAJOR";
                        primaryMajorId = reqList[0].MajorId;
                        warnings.Add($"Đề tài \"{title}\": trường chưa có ngành thứ 2 hoạt động nên chuyển về chế độ Một ngành.");
                    }
                }
                primaryMajorId = mode == "INTERDISCIPLINARY" ? null : primaryMajorId;
            }

            var content = new TopicContentRequest(
                title,
                string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
                string.IsNullOrWhiteSpace(item.ProblemStatement) ? null : item.ProblemStatement.Trim(),
                string.IsNullOrWhiteSpace(item.Objectives) ? null : item.Objectives.Trim(),
                string.IsNullOrWhiteSpace(item.ExpectedOutput) ? null : item.ExpectedOutput.Trim(),
                string.IsNullOrWhiteSpace(item.Domain) ? null : item.Domain.Trim(),
                (item.Technologies ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
                (item.Keywords ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
                mode,
                primaryMajorId,
                reqList);

            try
            {
                var createdTopic = await sender.Send(new CreateTopicCommand(new CreateTopicRequest(request.ProjectPeriodId, code, leadDeptId, content)), ct);
                created.Add(new(tempKey, createdTopic.Id, createdTopic.Code, createdTopic.Title, createdTopic.Status));
            }
            catch (Exception ex) when (ex is ConflictException or ValidationException or DomainException or ArgumentException)
            {
                db.ChangeTracker.Clear();
                var reason = ex is ValidationException vex
                    ? string.Join("; ", vex.Errors.SelectMany(kv => kv.Value).Take(3))
                    : ex.Message;
                skipped.Add(new(tempKey, code, title, string.IsNullOrWhiteSpace(reason) ? "Dữ liệu đề tài không đạt ràng buộc của kỳ đăng ký." : reason));
            }
        }

        var result = new ApplyTopicBatchSetupResultDto(created.Count, skipped.Count, false, created, skipped, warnings);
        db.AiSetupApplications.Add(new AiSetupApplication
        {
            ActorId = actorId,
            IdempotencyKey = request.IdempotencyKey,
            ScopeType = "TOPIC_BATCH",
            ProjectId = null,
            RunId = request.RunId,
            ResultJson = JsonSerializer.Serialize(result),
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(actorId, "AI_TOPIC_BATCH_SETUP_APPLIED", "PROJECT_PERIOD", request.ProjectPeriodId,
            new Dictionary<string, object?> { ["runId"] = request.RunId, ["createdCount"] = created.Count, ["skippedCount"] = skipped.Count }), ct);
        return result;
    }

    public async Task<ApplyColdTemplateSetupResultDto> ApplyColdTemplateSetupAsync(
        ApplyColdTemplateSetupRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId) throw new UnauthorizedException();
        if (request is null || request.IdempotencyKey == Guid.Empty)
            throw new ArgumentException("Thiếu mã xác thực chống trùng (idempotencyKey).");
        if (request.ProjectPeriodId <= 0)
            throw new ArgumentException("Cần chọn kỳ Đánh giá hoặc Bàn giao cuối kỳ.");
        if (request.Reports is null || request.Reports.Count == 0)
            throw new ArgumentException("Mẫu hồ sơ chấm nguội cần ít nhất một đầu mục báo cáo.");
        if (coldService is null)
            throw new ConflictException("Dịch vụ mẫu hồ sơ chấm nguội chưa sẵn sàng.");

        var replay = await db.AiSetupApplications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActorId == actorId && x.IdempotencyKey == request.IdempotencyKey && x.ScopeType == "COLD_TEMPLATE", ct);
        if (replay is not null)
        {
            var prev = JsonSerializer.Deserialize<ApplyColdTemplateSetupResultDto>(replay.ResultJson);
            if (prev is not null) return prev with { Replay = true };
        }

        await RequireDepartmentStaffActorAsync(actorId, ct);

        var scope = request.Scope?.Trim().ToUpperInvariant() == "COMMON" ? "COMMON" : "MAJOR_SPECIFIC";
        long? majorId = scope == "COMMON" ? null : request.MajorId;
        if (scope == "MAJOR_SPECIFIC" && !majorId.HasValue)
            throw new ArgumentException("Mẫu chấm nguội theo ngành cần chọn chuyên ngành áp dụng.");

        var warnings = new List<string>();
        var reports = new List<ColdReportInput>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var idx = 0;
        foreach (var r in request.Reports.Take(30))
        {
            if (string.IsNullOrWhiteSpace(r.Name)) continue;
            idx++;
            var normalizedCode = Regex.Replace((r.Code ?? $"REPORT_{idx}").Trim().ToUpperInvariant(), @"[^A-Z0-9_]", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(normalizedCode)) normalizedCode = $"REPORT_{idx}";
            if (normalizedCode.Length > 80) normalizedCode = normalizedCode[..80];
            if (!seen.Add(normalizedCode))
            {
                normalizedCode = $"{normalizedCode[..Math.Min(normalizedCode.Length, 72)]}_{idx}";
                seen.Add(normalizedCode);
                warnings.Add($"Đã tự thêm hậu tố chống trùng mã báo cáo: {normalizedCode}.");
            }
            var rName = r.Name.Trim();
            if (rName.Length > 200) rName = rName[..200];
            reports.Add(new ColdReportInput(normalizedCode, rName, r.Required));
        }
        if (reports.Count == 0)
            throw new ArgumentException("Cần ít nhất một đầu mục báo cáo hợp lệ.");
        if (!reports.Any(r => r.Required))
        {
            reports[0] = reports[0] with { Required = true };
            warnings.Add("Mẫu chấm nguội phải có tối thiểu 1 báo cáo bắt buộc nên hệ thống đã bật bắt buộc cho đầu mục đầu tiên.");
        }

        var templateName = string.IsNullOrWhiteSpace(request.Name) ? "Mẫu hồ sơ chấm nguội (AI)" : request.Name.Trim();
        if (templateName.Length > 200) templateName = templateName[..200];

        var created = await coldService.CreateTemplate(
            new CreateColdTemplateRequest(majorId, request.ProjectPeriodId, templateName, reports, scope), ct);

        var result = new ApplyColdTemplateSetupResultDto(
            created.Id, created.Name, created.Version, created.Scope ?? scope, created.Reports.Count, false,
            warnings.Count > 0 ? warnings : null);

        db.AiSetupApplications.Add(new AiSetupApplication
        {
            ActorId = actorId,
            IdempotencyKey = request.IdempotencyKey,
            ScopeType = "COLD_TEMPLATE",
            ProjectId = null,
            RunId = request.RunId,
            ResultJson = JsonSerializer.Serialize(result),
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(actorId, "AI_COLD_TEMPLATE_SETUP_APPLIED", "COLD_TEMPLATE", created.Id,
            new Dictionary<string, object?> { ["runId"] = request.RunId, ["version"] = created.Version, ["reportsCount"] = created.Reports.Count }), ct);
        return result;
    }

    public async Task<ApplyMilestoneTemplateSetupResultDto> ApplyMilestoneTemplateSetupAsync(
        ApplyMilestoneTemplateSetupRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId) throw new UnauthorizedException();
        if (request is null || request.IdempotencyKey == Guid.Empty)
            throw new ArgumentException("Thiếu mã xác thực chống trùng (idempotencyKey).");
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("Mẫu mốc đồ án cần tối thiểu một mốc.");
        if (request.Items.Count > 20)
            throw new ArgumentException("Tối đa 20 mốc trong một phiên bản mẫu.");

        var isAdmin = currentUser.Roles.Any(r => string.Equals(r, "ADMIN", StringComparison.OrdinalIgnoreCase))
            || await db.UserRoles.AsNoTracking().AnyAsync(r => r.UserId == actorId && r.Role.Code == "ADMIN", ct);
        if (!isAdmin)
            throw new ForbiddenException("Chỉ Quản trị viên (ADMIN) mới được tạo bản nháp Mẫu mốc đồ án.");

        var replay = await db.AiSetupApplications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActorId == actorId && x.IdempotencyKey == request.IdempotencyKey && x.ScopeType == "MILESTONE_TEMPLATE", ct);
        if (replay is not null)
        {
            var prev = JsonSerializer.Deserialize<ApplyMilestoneTemplateSetupResultDto>(replay.ResultJson);
            if (prev is not null) return prev with { Replay = true };
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? "Mẫu mốc đồ án (Nháp AI)" : request.Name.Trim();
        if (name.Length > 200) name = name[..200];

        long templateId;
        string templateName;
        if (request.ExistingTemplateId.HasValue && request.ExistingTemplateId.Value > 0)
        {
            templateId = request.ExistingTemplateId.Value;
            templateName = name;
        }
        else
        {
            var tpl = await sender.Send(new CreateMilestoneTemplateCommand(name, request.Description?.Trim()), ct);
            templateId = tpl.Id;
            templateName = tpl.Name;
        }

        var version = await sender.Send(new CreateMilestoneTemplateVersionCommand(templateId), ct);
        var addedCount = 0;
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (string.IsNullOrWhiteSpace(item.Title)) continue;
            var startOff = item.StartOffsetDays is >= 0 ? item.StartOffsetDays : null;
            var dueOff = item.DueOffsetDays is >= 0 ? item.DueOffsetDays : null;
            if (startOff.HasValue && dueOff.HasValue && dueOff < startOff) dueOff = startOff;
            await sender.Send(new AddMilestoneTemplateItemCommand(version.Id, new SaveMilestoneTemplateItemRequest(
                item.Title.Trim().Length > 200 ? item.Title.Trim()[..200] : item.Title.Trim(),
                item.Description?.Trim(),
                startOff,
                dueOff,
                i)), ct);
            addedCount++;
        }

        var result = new ApplyMilestoneTemplateSetupResultDto(templateId, version.Id, version.VersionNumber, templateName, "DRAFT", addedCount, false);
        db.AiSetupApplications.Add(new AiSetupApplication
        {
            ActorId = actorId,
            IdempotencyKey = request.IdempotencyKey,
            ScopeType = "MILESTONE_TEMPLATE",
            ProjectId = null,
            RunId = request.RunId,
            ResultJson = JsonSerializer.Serialize(result),
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(actorId, "AI_MILESTONE_TEMPLATE_SETUP_APPLIED", "MILESTONE_TEMPLATE_VERSION", version.Id,
            new Dictionary<string, object?> { ["runId"] = request.RunId, ["templateId"] = templateId, ["itemsCount"] = addedCount }), ct);
        return result;
    }

    private async Task<(long DepartmentId, long OrganizationId)> RequireDepartmentStaffActorAsync(long actorId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                OrganizationId = u.Department == null ? (long?)null : u.Department.OrganizationId,
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN")
            })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException("Tài khoản không hợp lệ.");

        if (!user.IsStaff)
            throw new ForbiddenException(user.IsAdmin
                ? "Tài khoản quản trị viên không có vai trò Cán bộ bộ môn (DEPARTMENT_STAFF) nên không thể lưu cấu hình chuyên môn này."
                : "Chỉ Cán bộ bộ môn mới có quyền áp dụng cấu hình này.");

        if (!user.DepartmentId.HasValue || !user.OrganizationId.HasValue)
            throw new ForbiddenException("Tài khoản Cán bộ bộ môn chưa được gắn khoa/bộ môn hoạt động.");

        return (user.DepartmentId.Value, user.OrganizationId.Value);
    }
}
