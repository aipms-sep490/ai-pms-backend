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

namespace AIPMS.Infrastructure.Services.AiSuite;

internal sealed partial class AiSuiteService
{
    public async Task<SubmissionPreflightDto> CheckSubmissionPreflightAsync(long projectId, SubmissionPreflightRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var (project, actor, actorName, scope) = await RequireProjectAccessAsync(projectId, ct);

        var target = string.IsNullOrWhiteSpace(request.Target) ? "FINAL_SUBMISSION" : request.Target.Trim().ToUpperInvariant();
        if (target is not ("FINAL_SUBMISSION" or "COLD" or "REVIEW_GATE"))
            throw Invalid("Loại kiểm tra không hợp lệ. Hỗ trợ: FINAL_SUBMISSION, COLD, REVIEW_GATE.");

        var deterministicChecks = new List<SubmissionPreflightCheckDto>();
        var itemsContext = new List<object>();
        var pdfExcerpts = new List<object>();
        int missingCount = 0;

        if (target == "FINAL_SUBMISSION")
        {
            var requiredIds = await db.Set<FinalSubmissionRequirement>().AsNoTracking()
                .Where(r => r.ProjectId == projectId)
                .Select(r => r.DeliverableId)
                .ToListAsync(ct);

            var deliverables = await ScopedDeliverablesQuery(projectId, scope)
                .Select(d => new
                {
                    d.Id,
                    d.Title,
                    d.Status,
                    d.EvidenceScope,
                    VersionIds = d.DeliverableVersions.Select(v => v.Id).ToList(),
                    LatestFiles = d.DeliverableVersions
                        .OrderByDescending(v => v.VersionNumber)
                        .Select(v => v.Files.Select(f => new { f.OriginalFileName, f.FileSizeBytes, f.StoragePath, f.ScanStatus }).ToList())
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var draft = await db.Set<FinalSubmissionDraft>().AsNoTracking()
                .Include(d => d.Items)
                .Where(d => d.ProjectId == projectId)
                .OrderByDescending(d => d.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            var submitted = await db.Set<FinalSubmission>().AsNoTracking()
                .Include(f => f.Items)
                .Where(f => f.ProjectId == projectId)
                .OrderByDescending(f => f.SubmittedAt)
                .FirstOrDefaultAsync(ct);

            // V4.0 TK-W3-01: Gate 3 passed check
            var gate3 = await db.ProjectGateInstances.AsNoTracking()
                .Where(g => g.ProjectId == projectId && (g.Sequence == 3 || g.Code == "G3" || g.BlocksDefense))
                .OrderByDescending(g => g.Sequence)
                .FirstOrDefaultAsync(ct);
            if (gate3 is not null)
            {
                var g3Passed = gate3.Status is "PASSED" or "OVERRIDDEN_PASSED";
                if (!g3Passed)
                {
                    missingCount++;
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        $"Điều kiện Cổng {gate3.Code} (GATE_3_NOT_PASSED)",
                        "FAIL",
                        $"Cổng {gate3.Code} hiện ở trạng thái {gate3.Status}; chưa đạt điều kiện mở khóa nộp cuối kỳ.",
                        "Hoàn tất thẩm định và đạt trạng thái PASSED tại Cổng 3 trước khi gửi yêu cầu khóa gói nộp cuối kỳ."));
                }
                else
                {
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        $"Điều kiện Cổng {gate3.Code}",
                        "PASS",
                        $"Cổng {gate3.Code} đã đạt trạng thái {gate3.Status}.",
                        null));
                }
            }

            // V4.0 TK-W3-01: Cold package locked check
            var coldPkg = await db.Set<ColdPackage>().AsNoTracking()
                .Where(c => c.ProjectId == projectId)
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync(ct);
            if (coldPkg is not null)
            {
                if (!string.Equals(coldPkg.Status, "LOCKED", StringComparison.OrdinalIgnoreCase))
                {
                    missingCount++;
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        "Gói hồ sơ Chấm nguội (COLD_PACKAGE_INCOMPLETE)",
                        "FAIL",
                        $"Gói Chấm nguội hiện ở trạng thái {coldPkg.Status} (chưa LOCKED).",
                        "Hoàn thiện và khóa gói hồ sơ Chấm nguội trước khi khóa gói nộp cuối kỳ để bảo vệ."));
                }
                else
                {
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        "Gói hồ sơ Chấm nguội (COLD)",
                        "PASS",
                        "Gói Chấm nguội đã ở trạng thái LOCKED.",
                        null));
                }
            }

            // V4.0 TK-W3-01: 2-step Pre-Defense Lock workflow status
            if (draft is not null)
            {
                var wf = (draft.LockWorkflowStatus ?? "OPEN").Trim().ToUpperInvariant();
                if (wf == "LOCKED" || submitted is not null)
                {
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        "Quy trình khóa nộp 2 bước (Pre-Defense Lock)",
                        "PASS",
                        $"Gói nộp cuối kỳ đã được khóa (trạng thái: {wf}{(submitted?.ColdManifestSha256 != null ? ", đã gắn ColdManifestSha256" : "")}).",
                        null));
                }
                else if (wf == "PENDING_SUPERVISOR_LOCK")
                {
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        "Quy trình khóa nộp 2 bước (Pre-Defense Lock)",
                        "WARN",
                        "Trưởng nhóm đã gửi yêu cầu khóa; đang chờ GVHD chính xác nhận khóa cuối cùng (bước 2/2).",
                        "Thông báo GVHD chính kiểm tra và xác nhận khóa gói nộp cuối kỳ."));
                }
                else
                {
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        "Quy trình khóa nộp 2 bước (Pre-Defense Lock)",
                        "WARN",
                        $"Bản nháp đang ở trạng thái {wf}. Theo quy trình V4.0, Trưởng nhóm phải gửi yêu cầu khóa (bước 1) để GVHD chính xác nhận khóa (bước 2).",
                        "Sau khi đủ tài liệu và tệp đạt ScanStatus=READY, Trưởng nhóm bấm Gửi yêu cầu khóa cho GVHD chính."));
                }
            }

            // V4.0 POL-05: Interdisciplinary INTEGRATION deliverable check
            var isInterdisciplinary = string.Equals(project.TrackCode, "IT_MKT", StringComparison.OrdinalIgnoreCase)
                || await db.ProjectMajors.AsNoTracking().CountAsync(pm => pm.ProjectId == projectId, ct) > 1;
            if (isInterdisciplinary)
            {
                var hasIntegration = deliverables.Any(d =>
                    string.Equals(d.EvidenceScope, "INTEGRATION", StringComparison.OrdinalIgnoreCase) && d.VersionIds.Count > 0);
                if (!hasIntegration)
                {
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        "Sản phẩm tích hợp liên ngành (POL-05 / INTEGRATION)",
                        "WARN",
                        "Đồ án liên ngành chưa ghi nhận hạng mục bàn giao có phạm vi INTEGRATION đã tải phiên bản.",
                        "Đảm bảo đính kèm minh chứng tích hợp IT-MKT (INT-*) để phục vụ bảo vệ liên ngành."));
                }
            }

            foreach (var reqId in requiredIds)
            {
                var deliv = deliverables.FirstOrDefault(d => d.Id == reqId);
                if (deliv is null && !scope.IsFullProjectAccess) continue; // Outside mentor scope
                var inDraft = (deliv is not null && draft?.Items.Any(i => deliv.VersionIds.Contains(i.DeliverableVersionId)) == true)
                    || submitted?.Items.Any(i => i.DeliverableId == reqId) == true;
                var unreadyFile = deliv?.LatestFiles?.FirstOrDefault(f => !string.Equals(f.ScanStatus, "READY", StringComparison.OrdinalIgnoreCase));
                if (deliv is null || deliv.VersionIds.Count == 0 || !inDraft)
                {
                    missingCount++;
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        deliv?.Title ?? $"Deliverable #{reqId}",
                        "FAIL",
                        "Hạng mục bắt buộc chưa có phiên bản đính kèm trong bản nộp cuối kỳ.",
                        "Tải lên phiên bản mới nhất và chọn đưa vào gói nộp cuối kỳ trước khi gửi yêu cầu khóa."));
                }
                else if (unreadyFile is not null)
                {
                    missingCount++;
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        deliv.Title,
                        "FAIL",
                        $"Tệp \"{unreadyFile.OriginalFileName}\" chưa qua kiểm duyệt an toàn (ScanStatus={unreadyFile.ScanStatus}).",
                        "Chờ hệ thống quét an toàn hoàn tất (READY) hoặc tải lại tệp sạch trước khi khóa nộp."));
                }
                else
                {
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        deliv.Title,
                        "PASS",
                        $"Đã có phiên bản nộp hợp lệ (trạng thái: {deliv.Status}, ScanStatus=READY).",
                        null));
                }
            }

            if (requiredIds.Count == 0 && deliverables.Count == 0)
            {
                missingCount++;
                deterministicChecks.Add(new SubmissionPreflightCheckDto(
                    "Sản phẩm / Deliverable nộp cuối kỳ",
                    "FAIL",
                    "Chưa có deliverable nào được tạo hoặc tải lên trong đồ án.",
                    "Tạo và tải lên tài liệu báo cáo, slide, mã nguồn theo yêu cầu của Khoa."));
            }

            foreach (var d in deliverables.Where(x => x.LatestFiles is { Count: > 0 }).Take(3))
            {
                var readyPdf = d.LatestFiles!.FirstOrDefault(f =>
                    string.Equals(f.ScanStatus, "READY", StringComparison.OrdinalIgnoreCase)
                    && f.OriginalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
                if (readyPdf is null) continue;
                var excerpt = await TryExtractPdfExcerptAsync(readyPdf.StoragePath, readyPdf.OriginalFileName, 1400, ct);
                if (excerpt is not null)
                    pdfExcerpts.Add(new { deliverable = d.Title, fileName = readyPdf.OriginalFileName, excerpt });
            }

            itemsContext.Add(new
            {
                requiredCount = requiredIds.Count,
                deliverablesCount = deliverables.Count,
                hasDraft = draft is not null,
                lockWorkflowStatus = draft?.LockWorkflowStatus ?? "NONE",
                alreadySubmitted = submitted is not null,
                hasColdManifest = !string.IsNullOrWhiteSpace(submitted?.ColdManifestSha256)
            });
        }
        else if (target == "COLD")
        {
            var pkg = await db.Set<ColdPackage>().AsNoTracking()
                .SingleOrDefaultAsync(p => p.ProjectId == projectId, ct);

            if (pkg is null)
            {
                missingCount++;
                deterministicChecks.Add(new SubmissionPreflightCheckDto(
                    "Gói nộp báo cáo COLD",
                    "FAIL",
                    "Chưa khởi tạo gói nộp COLD cho đồ án này.",
                    "Mở màn hình Báo cáo COLD để kiểm tra kỳ nộp và khởi tạo checklist."));
            }
            else
            {
                var allChecklist = await db.Set<ColdChecklistItem>().AsNoTracking()
                    .Where(c => c.PackageId == pkg.Id)
                    .ToListAsync(ct);
                var checklist = allChecklist
                    .Where(c => scope.CanAccessEvidenceScope(c.Scope, c.MajorId))
                    .ToList();

                var itemIds = checklist.Select(c => c.Id).ToArray();
                var subs = await db.Set<ColdSubmission>().AsNoTracking()
                    .Include(s => s.Files)
                    .Where(s => itemIds.Contains(s.ChecklistItemId))
                    .ToListAsync(ct);

                var allColdFileIds = subs.SelectMany(s => s.Files).Select(f => f.FileId).Distinct().ToArray();
                var fileScanMap = allColdFileIds.Length == 0
                    ? new Dictionary<long, string>()
                    : await db.Files.AsNoTracking()
                        .Where(f => allColdFileIds.Contains(f.Id))
                        .ToDictionaryAsync(f => f.Id, f => f.ScanStatus, ct);

                foreach (var item in checklist)
                {
                    if (string.Equals(item.WaivedStatus, "WAIVED_WITHDRAWN", StringComparison.OrdinalIgnoreCase))
                    {
                        deterministicChecks.Add(new SubmissionPreflightCheckDto(
                            item.Name,
                            "PASS",
                            "Đầu mục đã được miễn trừ do thành viên rút học phần (WAIVED_WITHDRAWN).",
                            null));
                        continue;
                    }

                    var latest = subs.Where(s => s.ChecklistItemId == item.Id).OrderByDescending(s => s.Version).FirstOrDefault();
                    var hasFile = latest is not null && latest.Files.Count > 0;
                    if (item.Required && !hasFile)
                    {
                        missingCount++;
                        deterministicChecks.Add(new SubmissionPreflightCheckDto(
                            item.Name,
                            "FAIL",
                            $"Báo cáo bắt buộc ({item.ReportType}) chưa có tệp đính kèm.",
                            "Tải lên đúng mẫu báo cáo theo template của Khoa."));
                    }
                    else if (hasFile)
                    {
                        var unready = latest!.Files.FirstOrDefault(f =>
                            fileScanMap.TryGetValue(f.FileId, out var st) && !string.Equals(st, "READY", StringComparison.OrdinalIgnoreCase));
                        if (unready is not null)
                        {
                            missingCount++;
                            deterministicChecks.Add(new SubmissionPreflightCheckDto(
                                item.Name,
                                "FAIL",
                                $"Tệp \"{unready.FileName}\" chưa đạt trạng thái quét an toàn READY ({fileScanMap[unready.FileId]}).",
                                "Chờ hệ thống quét an toàn hoàn tất hoặc tải lại tệp hợp lệ."));
                            continue;
                        }

                        deterministicChecks.Add(new SubmissionPreflightCheckDto(
                            item.Name,
                            "PASS",
                            $"Đã tải lên {latest.Files.Count} tệp (trạng thái: {latest.Status}).",
                            null));
                        var firstPdf = latest.Files.FirstOrDefault(f =>
                            f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                            && (!fileScanMap.TryGetValue(f.FileId, out var st) || string.Equals(st, "READY", StringComparison.OrdinalIgnoreCase)));
                        if (firstPdf is not null && pdfExcerpts.Count < 3)
                        {
                            var excerpt = await TryExtractPdfExcerptAsync(firstPdf.StorageKey, firstPdf.FileName, 1400, ct);
                            if (excerpt is not null)
                                pdfExcerpts.Add(new { checklistItem = item.Name, fileName = firstPdf.FileName, excerpt });
                        }
                    }
                }
                itemsContext.Add(new { packageStatus = pkg.Status, deadline = pkg.Deadline.ToString("O"), checklistCount = checklist.Count });
            }
        }
        else // REVIEW_GATE
        {
            var gateQuery = db.ProjectGateInstances.AsNoTracking()
                .Include(g => g.Requirements).ThenInclude(r => r.Evidence)
                .Where(g => g.ProjectId == projectId);
            var gate = request.TargetId is long gid && gid > 0
                ? await gateQuery.SingleOrDefaultAsync(g => g.Id == gid, ct)
                : await gateQuery.OrderBy(g => g.Sequence).FirstOrDefaultAsync(g => g.Status != "PASSED", ct)
                  ?? await gateQuery.OrderByDescending(g => g.Sequence).FirstOrDefaultAsync(ct);

            if (gate is null)
            {
                missingCount++;
                deterministicChecks.Add(new SubmissionPreflightCheckDto(
                    "Cổng kiểm duyệt (Review Gate)",
                    "WARN",
                    "Chưa có cổng kiểm duyệt nào được gắn cho đồ án này.",
                    "Kiểm tra cấu hình cổng kiểm duyệt của kỳ học."));
            }
            else
            {
                var gateDecided = IsGateDecided(gate.Status);
                var visibleReqs = gate.Requirements
                    .Where(r => scope.CanAccessEvidenceScope(r.Scope, r.MajorId))
                    .ToList();

                foreach (var req in visibleReqs)
                {
                    if (string.Equals(req.VerificationStatus, "WAIVED_WITHDRAWN", StringComparison.OrdinalIgnoreCase))
                    {
                        deterministicChecks.Add(new SubmissionPreflightCheckDto(
                            $"{gate.Code} · {req.Title}",
                            "PASS",
                            "Yêu cầu cá nhân đã được miễn trừ (WAIVED_WITHDRAWN).",
                            null));
                        continue;
                    }

                    var latestEv = req.Evidence.OrderByDescending(e => e.Version).FirstOrDefault();
                    if (req.Required && latestEv is null)
                    {
                        missingCount++;
                        deterministicChecks.Add(new SubmissionPreflightCheckDto(
                            $"{gate.Code} · {req.Title}",
                            "FAIL",
                            "Yêu cầu bắt buộc của cổng chưa có minh chứng nộp lên.",
                            "Nộp tệp hoặc liên kết minh chứng trước thời điểm kiểm duyệt cổng."));
                    }
                    else if (latestEv is not null)
                    {
                        if (!string.Equals(latestEv.ScanStatus, "READY", StringComparison.OrdinalIgnoreCase))
                        {
                            missingCount++;
                            deterministicChecks.Add(new SubmissionPreflightCheckDto(
                                $"{gate.Code} · {req.Title}",
                                "FAIL",
                                $"Minh chứng \"{latestEv.ArtifactName}\" chưa đạt trạng thái quét an toàn READY (ScanStatus={latestEv.ScanStatus}).",
                                "Chờ quét an toàn hoàn tất hoặc tải lại tệp hợp lệ."));
                            continue;
                        }

                        var st = req.VerificationStatus == "REJECTED" ? "FAIL" : "PASS";
                        if (st == "FAIL") missingCount++;
                        var safeFeedback = gateDecided ? req.ReviewerFeedback : null;
                        deterministicChecks.Add(new SubmissionPreflightCheckDto(
                            $"{gate.Code} · {req.Title}",
                            st,
                            $"Đã nộp minh chứng \"{latestEv.ArtifactName}\" (xác minh: {req.VerificationStatus}, ScanStatus=READY).",
                            st == "FAIL" ? safeFeedback ?? "Bổ sung lại minh chứng theo yêu cầu hội đồng." : null));
                    }
                }

                // V4.0: Check unresolved CRITICAL evidence-anchored comments
                var unresolvedCritical = await db.Set<EvidenceAnchoredComment>().AsNoTracking()
                    .CountAsync(c => c.ProjectId == projectId
                        && c.GateInstanceId == gate.Id
                        && c.Severity == "CRITICAL"
                        && c.Status != "RESOLVED", ct);
                if (unresolvedCritical > 0)
                {
                    missingCount++;
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        $"{gate.Code} · Nhận xét mức CRITICAL chưa xử lý (UNRESOLVED_CRITICAL_COMMENTS)",
                        "FAIL",
                        $"Còn {unresolvedCritical} nhận xét mức nghiêm trọng (CRITICAL) trên minh chứng chưa được đánh dấu RESOLVED.",
                        "Xử lý toàn bộ nhận xét CRITICAL và cập nhật trạng thái RESOLVED trước khi gửi lại vòng thẩm định."));
                }

                itemsContext.Add(new { gate.Code, gate.Title, gate.Status, gate.ReviewEligibility, unresolvedCritical });
            }
        }

        // Scope DepartmentDocumentTemplate strictly to the project's departments (no cross-org/cross-dept leak)
        var academicScope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: false);
        var templateQuery = db.Set<DepartmentDocumentTemplate>().AsNoTracking()
            .Where(t => t.Status == "PUBLISHED");
        if (academicScope.DepartmentIds.Count > 0)
            templateQuery = templateQuery.Where(t => academicScope.DepartmentIds.Contains(t.DepartmentId));
        else
            templateQuery = templateQuery.Where(t => false);

        var templates = await templateQuery
            .OrderByDescending(t => t.PublishedAt)
            .Take(8)
            .Select(t => new { t.Code, t.Title, t.FileName })
            .ToListAsync(ct);

        var systemPrompt = AiSuiteIdentity + " " +
            $"[AI_SUITE_SUBMISSION_PREFLIGHT] Kiểm duyệt gắt gao (Pre-flight Check) trước khi sinh viên khóa nộp hồ sơ loại {target}. " +
            "Dựa trên kết quả kiểm tra checklist bắt buộc, quy trình khóa 2 bước V4.0, template của Khoa và trích đoạn PDF thực tế (nếu có), hãy đánh giá điểm sẵn sàng (0–100), chỉ ra các lỗi nội dung/trình bày cần sửa ngay và tổng hợp danh sách kiểm tra. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"readinessScore\":number,\"verdict\":\"READY\"|\"WARNING\"|\"BLOCKING_ISSUES\",\"summary\":string,\"checks\":[{\"item\":string,\"status\":\"PASS\"|\"WARN\"|\"FAIL\",\"detail\":string,\"fixSuggestion\":string|null}],\"priorityFixes\":string[]}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            target,
            projectCode = project.Code,
            projectTitle = project.Title,
            projectDescription = Clip(project.Description, 900),
            missingCount,
            deterministicChecks,
            context = itemsContext,
            departmentTemplates = templates,
            pdfExcerpts
        }, ct);

        var status = ResolveGenerationStatus(true);
        int readiness = missingCount == 0 ? 85 : Math.Max(20, 80 - missingCount * 15);
        string verdict = missingCount == 0 ? "READY" : "BLOCKING_ISSUES";
        string summary = missingCount == 0
            ? $"Hồ sơ {target} đã đáp ứng các điều kiện cơ bản."
            : $"Phát hiện {missingCount} hạng mục bắt buộc chưa đạt cho {target}.";
        var mergedChecks = new List<SubmissionPreflightCheckDto>(deterministicChecks);
        string[] fixes = missingCount > 0 ? [$"Hoàn thiện ngay {missingCount} mục đang báo lỗi FAIL trước khi nộp."] : [];

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            readiness = ReadInt(r, "readinessScore", readiness, 0, 100);
            if (missingCount > 0) readiness = Math.Min(readiness, 65);
            var v = ReadString(r, "verdict", verdict, 24).ToUpperInvariant();
            if (v is "READY" or "WARNING" or "BLOCKING_ISSUES") verdict = missingCount > 0 ? "BLOCKING_ISSUES" : v;
            summary = ReadString(r, "summary", summary, 1800);
            fixes = ReadStringArray(r, "priorityFixes", 6, 300);

            if (r.TryGetProperty("checks", out var cArr) && cArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cArr.EnumerateArray().Take(12))
                {
                    var itemName = ReadString(c, "item", "", 200);
                    if (string.IsNullOrWhiteSpace(itemName) || mergedChecks.Any(x => string.Equals(x.Item, itemName, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var st = ReadString(c, "status", "WARN", 12).ToUpperInvariant();
                    if (st is not ("PASS" or "WARN" or "FAIL")) st = "WARN";
                    mergedChecks.Add(new SubmissionPreflightCheckDto(itemName, st, ReadString(c, "detail", "", 400), ReadNullableString(c, "fixSuggestion", 300)));
                }
            }
        }
        else
        {
            status = "FALLBACK";
        }

        var snapPayload = new { target, readiness, verdict, summary, checks = mergedChecks, fixes };
        var meta = await SaveSnapshotAsync("SUBMISSION_PREFLIGHT", "PROJECT", projectId, projectId, null, status, $"Pre-flight {target} · {readiness}/100", snapPayload, actor, actorName, ct);
        return new SubmissionPreflightDto(projectId, target, readiness, verdict, summary, mergedChecks, fixes, meta);
    }
}
