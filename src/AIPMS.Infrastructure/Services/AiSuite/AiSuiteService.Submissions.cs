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
    public async Task<SubmissionPreflightDto> CheckSubmissionPreflightAsync(long projectId, SubmissionPreflightRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var (project, actor, actorName) = await RequireProjectAccessAsync(projectId, ct);

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

            var deliverables = await db.Deliverables.AsNoTracking()
                .Where(d => d.Milestone != null && d.Milestone.ProjectId == projectId)
                .Select(d => new
                {
                    d.Id,
                    d.Title,
                    d.Status,
                    VersionIds = d.DeliverableVersions.Select(v => v.Id).ToList(),
                    LatestFile = d.DeliverableVersions
                        .OrderByDescending(v => v.VersionNumber)
                        .SelectMany(v => v.Files)
                        .Select(f => new { f.OriginalFileName, f.FileSizeBytes, f.StoragePath })
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

            foreach (var reqId in requiredIds)
            {
                var deliv = deliverables.FirstOrDefault(d => d.Id == reqId);
                var inDraft = (deliv is not null && draft?.Items.Any(i => deliv.VersionIds.Contains(i.DeliverableVersionId)) == true)
                    || submitted?.Items.Any(i => i.DeliverableId == reqId) == true;
                if (deliv is null || deliv.VersionIds.Count == 0 || !inDraft)
                {
                    missingCount++;
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        deliv?.Title ?? $"Deliverable #{reqId}",
                        "FAIL",
                        "Hạng mục bắt buộc chưa có phiên bản đính kèm trong bản nộp cuối kỳ.",
                        "Tải lên phiên bản mới nhất và chọn đưa vào gói nộp cuối kỳ trước khi xác nhận nộp."));
                }
                else
                {
                    deterministicChecks.Add(new SubmissionPreflightCheckDto(
                        deliv.Title,
                        "PASS",
                        $"Đã có phiên bản nộp (trạng thái: {deliv.Status}).",
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

            foreach (var d in deliverables.Where(x => x.LatestFile != null).Take(3))
            {
                var excerpt = await TryExtractPdfExcerptAsync(d.LatestFile!.StoragePath, d.LatestFile.OriginalFileName, 1400, ct);
                if (excerpt is not null)
                    pdfExcerpts.Add(new { deliverable = d.Title, fileName = d.LatestFile.OriginalFileName, excerpt });
            }

            itemsContext.Add(new { requiredCount = requiredIds.Count, deliverablesCount = deliverables.Count, hasDraft = draft is not null, alreadySubmitted = submitted is not null });
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
                var checklist = await db.Set<ColdChecklistItem>().AsNoTracking()
                    .Where(c => c.PackageId == pkg.Id)
                    .ToListAsync(ct);

                var itemIds = checklist.Select(c => c.Id).ToArray();
                var subs = await db.Set<ColdSubmission>().AsNoTracking()
                    .Include(s => s.Files)
                    .Where(s => itemIds.Contains(s.ChecklistItemId))
                    .ToListAsync(ct);

                foreach (var item in checklist)
                {
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
                        deterministicChecks.Add(new SubmissionPreflightCheckDto(
                            item.Name,
                            "PASS",
                            $"Đã tải lên {latest!.Files.Count} tệp (trạng thái: {latest.Status}).",
                            null));
                        var firstPdf = latest.Files.FirstOrDefault(f => f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
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
                foreach (var req in gate.Requirements)
                {
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
                        var st = req.VerificationStatus == "REJECTED" ? "FAIL" : "PASS";
                        if (st == "FAIL") missingCount++;
                        deterministicChecks.Add(new SubmissionPreflightCheckDto(
                            $"{gate.Code} · {req.Title}",
                            st,
                            $"Đã nộp minh chứng \"{latestEv.ArtifactName}\" (xác minh: {req.VerificationStatus}).",
                            st == "FAIL" ? req.ReviewerFeedback ?? "Bổ sung lại minh chứng theo yêu cầu hội đồng." : null));
                    }
                }
                itemsContext.Add(new { gate.Code, gate.Title, gate.Status, gate.ReviewEligibility });
            }
        }

        var templates = await db.Set<DepartmentDocumentTemplate>().AsNoTracking()
            .Where(t => t.Status == "PUBLISHED")
            .OrderByDescending(t => t.PublishedAt)
            .Take(8)
            .Select(t => new { t.Code, t.Title, t.FileName })
            .ToListAsync(ct);

        var systemPrompt = AiSuiteIdentity + " " +
            $"[AI_SUITE_SUBMISSION_PREFLIGHT] Kiểm duyệt gắt gao (Pre-flight Check) trước khi sinh viên khóa nộp hồ sơ loại {target}. " +
            "Dựa trên kết quả kiểm tra checklist bắt buộc, template của Khoa và trích đoạn PDF thực tế (nếu có), hãy đánh giá điểm sẵn sàng (0–100), chỉ ra các lỗi nội dung/trình bày cần sửa ngay và tổng hợp danh sách kiểm tra. " +
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
