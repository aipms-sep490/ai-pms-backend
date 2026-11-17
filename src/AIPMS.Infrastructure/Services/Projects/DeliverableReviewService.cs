using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AcademicKnowledge.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class DeliverableReviewService(AipmsDbContext db, AiActorScope access, ICurrentUser current,
    IFileStorage storage, IAuditTrail audit, TimeProvider clock, IConfiguration configuration,
    IAiTextGenerationProvider provider) : IDeliverableReviewService
{
    private void Enabled()
    {
        if (!configuration.GetValue<bool>("AiAssistant:DeliverableReviewEnabled"))
            throw new ConflictException("Kiểm tra báo cáo bằng AI chưa được bật.", "AI_REVIEW_DISABLED");
    }

    private async Task<(AiScope Scope, AIPMS.Infrastructure.Persistence.Generated.Models.DeliverableVersion Version, long Department)> Read(long projectId, long versionId, CancellationToken ct)
    {
        Enabled();
        var scope = await access.Read(projectId, ct);
        var version = await DeliverableReviewAccess.VisibleVersions(db, scope).Include(v => v.Deliverable).ThenInclude(d => d.Project).Include(v => v.Files)
            .SingleOrDefaultAsync(v => v.Id == versionId, ct) ?? throw new NotFoundException("Deliverable version", versionId);
        var actor = await db.Users.AsNoTracking().SingleAsync(u => u.Id == scope.Actor, ct);
        var department = version.Deliverable.TargetMajorId.HasValue ?
            await db.Majors.Where(m => m.Id == version.Deliverable.TargetMajorId).Select(m => (long?)m.DepartmentId).SingleAsync(ct) : actor.DepartmentId;
        return (scope, version, department ?? throw new ForbiddenException("Cần có khoa để chọn rubric."));
    }

    private async Task<AssistantRubricOption[]> Rubrics(long projectId, long department, CancellationToken ct)
    {
        var semester = await db.Projects.Where(p => p.Id == projectId).Select(p => p.Team.AcademicSemesterId).SingleAsync(ct);
        return await (from r in db.Rubrics.AsNoTracking()
            join v in db.Set<RubricVersion>().AsNoTracking() on r.Id equals v.RubricId
            where r.DepartmentId == department && r.AcademicSemesterId == semester && r.IsActive && v.Status == "PUBLISHED"
            orderby r.Name, v.VersionNumber descending
            select new AssistantRubricOption(r.Id, r.Name, v.VersionNumber,
                db.Set<EvaluationSchemeComponent>().Any(c => c.RubricId == r.Id &&
                    db.Set<EvaluationScheme>().Any(s => s.Id == c.SchemeId && s.ProjectId == projectId && s.Status == "PUBLISHED"))))
            .Take(100).ToArrayAsync(ct);
    }

    public async Task<DeliverableReviewOptions> OptionsAsync(long projectId, long versionId, CancellationToken ct)
    {
        var (_, version, department) = await Read(projectId, versionId, ct);
        var checklist = await (from item in db.Set<ColdChecklistItem>().AsNoTracking()
            join package in db.Set<ColdPackage>().AsNoTracking() on item.PackageId equals package.Id
            where package.ProjectId == projectId && item.Scope == (version.Deliverable.EvidenceScope ?? "COMMON")
                && (version.Deliverable.TargetMajorId == null || item.MajorId == version.Deliverable.TargetMajorId)
                && item.StudentId == version.Deliverable.TargetStudentId
            orderby item.Name
            select new DeliverableChecklistOption(item.Id, item.Name, item.TemplateVersion)).Take(100).ToArrayAsync(ct);
        return new(version.Deliverable.EvidenceScope ?? "COMMON", await Rubrics(projectId, department, ct), checklist);
    }

    public async Task<DeliverableReviewResponseDto> ReviewAsync(long projectId, long versionId, DeliverableReviewRequest request, CancellationToken ct)
    {
        var started = clock.GetUtcNow().UtcDateTime;
        var (scope, version, department) = await Read(projectId, versionId, ct);
        if (request.Scope != (version.Deliverable.EvidenceScope ?? "COMMON")) throw new ArgumentException("Phạm vi phải trùng với hạng mục đã chọn.");
        if (!await db.TeamMembers.AnyAsync(m => m.TeamId == version.Deliverable.Project.TeamId && m.UserId == current.UserId && m.LeftAt == null, ct))
            throw new ForbiddenException("Chỉ thành viên hiện tại của nhóm được yêu cầu kiểm tra.");
        if (request.RubricId <= 0 || (request.ChecklistId.HasValue && request.ChecklistId <= 0)) throw new ArgumentException("Rubric/checklist không hợp lệ.");
        var rubric = (await Rubrics(projectId, department, ct)).SingleOrDefault(r => r.Id == request.RubricId);
        var options = await OptionsAsync(projectId, versionId, ct);
        if (request.ChecklistId.HasValue && !options.Checklists.Any(c => c.Id == request.ChecklistId))
            throw new NotFoundException("Checklist trong phạm vi hạng mục", request.ChecklistId);
        var checklist = request.ChecklistId.HasValue ? await db.Set<ColdChecklistItem>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ChecklistId, ct)
            ?? throw new NotFoundException("Checklist", request.ChecklistId) : null;
        var finalRequirementTitles = await (from requirement in db.Set<FinalSubmissionRequirement>().AsNoTracking()
            join deliverable in db.Deliverables.AsNoTracking() on requirement.DeliverableId equals deliverable.Id
            where requirement.ProjectId == projectId && requirement.DeliverableId == version.DeliverableId
            select deliverable.Title).ToArrayAsync(ct);
        var criteria = rubric == null ? [] : await db.RubricCriteria.AsNoTracking().Where(c => c.RubricId == rubric.Id).OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new ReviewCriterionSource(c.Id, c.ParentId, c.Criterion.Name, c.Criterion.Description)).Take(101).ToArrayAsync(ct);
        if (criteria.Length > 100) throw new ArgumentException("Tối đa 100 tiêu chí mỗi lần kiểm tra.");
        var files = version.Files.OrderBy(f => f.Id).ToArray();
        if (files.Length > 4 || files.Any(f => f.FileSizeBytes is <= 0 or > AssistantAttachmentReader.MaxFileBytes) ||
            files.Sum(f => f.FileSizeBytes) > AssistantAttachmentReader.MaxTotalBytes)
            throw new ArgumentException("Tối đa 4 tệp, mỗi tệp 10 MB, tổng 20 MB.");
        if (files.Any(f => !new[] { ".pdf", ".docx", ".txt", ".md" }.Contains(Path.GetExtension(f.OriginalFileName).ToLowerInvariant())))
            throw new ArgumentException("Hỗ trợ PDF, DOCX, TXT và Markdown.");
        var run = new AiRun { ProjectId = projectId, ActorId = scope.Actor, Kind = "DELIVERABLE_REVIEW",
            Status = "RUNNING", ModelVersion = "PENDING", ConfigVersion = "DELIVERABLE_REVIEW_V1",
            ScopeHash = scope.Hash, StartedAt = started };
        db.Add(run);
        await db.SaveChangesAsync(ct);
        try
        {
        string? unavailable = rubric == null ? "Rubric chưa được công bố đúng khoa và kỳ học." :
            criteria.Length == 0 ? "Rubric chưa có tiêu chí." : files.Length == 0 ? "Phiên bản chưa có tệp." : null;
        IReadOnlyList<AssistantAttachment> attachments = [];
        if (unavailable == null)
        {
            try
            {
                var uploads = new List<AssistantUpload>();
                foreach (var file in files)
                {
                    await using var input = await storage.OpenReadAsync(file.StoragePath, ct);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[81920]; int n;
                    while ((n = await input.ReadAsync(chunk, ct)) > 0)
                    {
                        if (buffer.Length + n > AssistantAttachmentReader.MaxFileBytes) throw new ArgumentException("Tệp vượt quá 10 MB.");
                        buffer.Write(chunk, 0, n);
                    }
                    var bytes = buffer.ToArray();
                    if (bytes.LongLength != file.FileSizeBytes || (!string.IsNullOrEmpty(file.ChecksumSha256) &&
                        !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.ChecksumSha256, StringComparison.OrdinalIgnoreCase)))
                        throw new IOException();
                    uploads.Add(new(file.OriginalFileName, bytes));
                }
                attachments = AssistantAttachmentReader.Read(uploads, ct);
            }
            catch (ArgumentException ex) { unavailable = ex.Message; }
            catch (IOException) { unavailable = "Không đọc được phiên bản lưu trữ hoặc checksum không khớp. Hãy nộp lại tệp."; }
        }
        var required = await db.Set<FinalSubmissionRequirement>().AnyAsync(r => r.ProjectId == projectId && r.DeliverableId == version.DeliverableId, ct);
        var requiredVersion = required ? await db.Set<FinalSubmissionRequirements>().Where(r => r.ProjectId == projectId).Select(r => (Guid?)r.ConcurrencyToken).SingleOrDefaultAsync(ct) : null;
        var assessment = unavailable == null ? await DeliverableReviewAnalyzer.Analyze(provider, configuration, criteria,
            string.Join("\n\n", attachments.Select(a => a.Text)), checklist is null ? null :
                $"{checklist.ReportType} | {checklist.Name} | required={checklist.Required} | scope={checklist.Scope}", finalRequirementTitles, ct) :
            new ReviewAssessment([], "INSUFFICIENT_EVIDENCE", unavailable, "Chưa đủ căn cứ, không suy đoán nội dung.", "NONE");
        var rows = assessment.Criteria;
        var percent = DeliverableReviewAnalyzer.ReadinessPercent(criteria, rows);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var latest = await Read(projectId, versionId, ct);
        if (latest.Scope.Hash != scope.Hash || latest.Department != department || FileSignature(latest.Version) != FileSignature(version))
            throw new ForbiddenException("Quyền hoặc phiên bản đã thay đổi trong khi phân tích.");
        if (rubric != null && !(await Rubrics(projectId, department, ct)).Contains(rubric))
            throw new ConflictException("Rubric đã thay đổi. Vui lòng thử lại.");
        var latestCriteria = rubric == null ? [] : await db.RubricCriteria.AsNoTracking()
            .Where(c => c.RubricId == rubric.Id).OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new ReviewCriterionSource(c.Id, c.ParentId, c.Criterion.Name, c.Criterion.Description)).ToArrayAsync(ct);
        var latestChecklist = checklist == null ? null : await db.Set<ColdChecklistItem>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == checklist.Id, ct);
        var latestRequired = await db.Set<FinalSubmissionRequirement>().AnyAsync(r => r.ProjectId == projectId && r.DeliverableId == version.DeliverableId, ct);
        var latestRequiredVersion = latestRequired ? await db.Set<FinalSubmissionRequirements>().Where(r => r.ProjectId == projectId).Select(r => (Guid?)r.ConcurrencyToken).SingleOrDefaultAsync(ct) : null;
        if (!criteria.SequenceEqual(latestCriteria) || JsonSerializer.Serialize(checklist) != JsonSerializer.Serialize(latestChecklist)
            || required != latestRequired || requiredVersion != latestRequiredVersion)
            throw new ConflictException("Yêu cầu đối chiếu đã thay đổi trong khi phân tích. Vui lòng thử lại.");
        run.Status = assessment.Status;
        run.CompletedAt = now;
        run.ModelVersion = assessment.Model[..Math.Min(64, assessment.Model.Length)];
        run.EvidenceJson = JsonSerializer.Serialize(new[] { new EvidenceReferenceDto("DELIVERABLE_VERSION", $"DV-{versionId}", version.Deliverable.Title) });
        run.ErrorCode = assessment.Status == "FALLBACK" ? "AI_PROVIDER_FALLBACK" : unavailable != null ? "AI_INSUFFICIENT_EVIDENCE" : null;
        var citations = rubric == null ? new List<DeliverableReviewCitationDto>() : criteria.Select(c => new DeliverableReviewCitationDto(rubric.Title, $"v{rubric.Version}", c.Name)).ToList();
        if (checklist != null) citations.Add(new("Checklist COLD", $"v{checklist.TemplateVersion}", checklist.Name));
        if (required) citations.Add(new("Yêu cầu bàn giao cuối kỳ", "Bản áp dụng khi phân tích", version.Deliverable.Title));
        var result = new DeliverableReviewResponseDto(run.Id, projectId, versionId, assessment.Status, unavailable != null ? "UNKNOWN" : percent == 100 ? "READY" : percent > 0 ? "PARTIAL" : "NOT_READY", percent,
            assessment.Summary, rows, citations, unavailable != null,
            "Nhận xét tham khảo, không phải điểm chính thức. " + assessment.Limitation, assessment.Status);
        run.ResultJson = JsonSerializer.Serialize(result); await db.SaveChangesAsync(ct);
        db.Add(new AiDeliverableReviewRun { Id = run.Id, ProjectId = projectId, DeliverableId = version.DeliverableId, DeliverableVersionId = versionId,
            RubricId = request.RubricId, RubricVersion = rubric?.Version, ChecklistId = checklist?.Id, ChecklistVersion = checklist?.TemplateVersion,
            EvidenceSnapshot = JsonSerializer.Serialize(new { rubric, criteria, checklist, required, requiredVersion,
                files = files.Select(f => new { f.Id, f.OriginalFileName, f.ChecksumSha256, f.FileSizeBytes }),
                attachments = attachments.Select(a => new { a.Name, a.Text, a.Checksum }) }) });
        db.AddRange(rows.Select(x => new AiDeliverableReviewCriterion { RunId = run.Id, CriterionId = x.CriterionId, CriterionName = x.CriterionName, Status = x.Status }));
        db.AddRange(rows.SelectMany(x => x.Findings.Select(f => new AiDeliverableReviewFinding { RunId = run.Id, CriterionId = x.CriterionId, Kind = "FINDING", Content = f })).Concat(rows.SelectMany(x => x.MissingEvidence.Select(f => new AiDeliverableReviewFinding { RunId = run.Id, CriterionId = x.CriterionId, Kind = "MISSING_EVIDENCE", Content = f }))));
        db.AddRange(citations.Select(c => new AiDeliverableReviewCitation { RunId = run.Id, Title = c.Title, Version = c.Version, Section = c.Section }));
        db.AddRange(rows.SelectMany(r => r.Suggestions.Select(s => new AiDeliverableReviewFinding { RunId = run.Id, CriterionId = r.CriterionId, Kind = "SUGGESTION", Content = s })));
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(scope.Actor, "AI_DELIVERABLE_REVIEW_COMPLETED", "AI_RUN", run.Id, new Dictionary<string, object?> { ["projectId"] = projectId, ["deliverableVersionId"] = versionId }), ct);
        await tx.CommitAsync(ct);
        return result;
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                // The completion transaction has rolled back; retain only a sanitized failure marker.
                await db.Set<AiRun>().Where(r => r.Id == run.Id && r.Status == "RUNNING")
                    .ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, "FAILED")
                        .SetProperty(r => r.ErrorCode, "AI_REVIEW_INTERRUPTED")
                        .SetProperty(r => r.CompletedAt, clock.GetUtcNow().UtcDateTime), cleanup.Token);
            }
            catch { /* Preserve the RUNNING reservation if storage is unavailable. */ }
            throw;
        }
    }

    public async Task<PagedResult<DeliverableReviewResponseDto>> ListAsync(long projectId, long? versionId, int page, int pageSize, CancellationToken ct)
    {
        Enabled(); var scope = await access.Read(projectId, ct);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue) throw new ArgumentException("Invalid pagination.");
        var visible = DeliverableReviewAccess.VisibleVersions(db, scope).Select(v => v.Id);
        var q = from review in db.AiDeliverableReviewRuns.AsNoTracking()
                join run in db.Set<AiRun>().AsNoTracking() on review.Id equals run.Id
                where run.ProjectId == projectId && run.ActorId == scope.Actor && run.ScopeHash == scope.Hash &&
                    run.Status != "RUNNING" && run.Status != "FAILED" && run.ResultJson != null &&
                    visible.Contains(review.DeliverableVersionId) && (versionId == null || review.DeliverableVersionId == versionId)
                select run;
        var total = await q.LongCountAsync(ct);
        var all = await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(all.Select(x => JsonSerializer.Deserialize<DeliverableReviewResponseDto>(x.ResultJson!)!).ToArray(), page, pageSize, total);
    }

    public async Task<DeliverableReviewResponseDto> GetAsync(long projectId, long runId, CancellationToken ct)
    {
        Enabled(); var scope = await access.Read(projectId, ct);
        var row = await db.Set<AiRun>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId && x.ProjectId == projectId && x.ActorId == scope.Actor && x.Kind == "DELIVERABLE_REVIEW" && x.ScopeHash == scope.Hash, ct)
            ?? throw new NotFoundException("AI review", runId);
        if (row.Status is "RUNNING" or "FAILED") throw new ConflictException("Lần kiểm tra chưa có kết quả. Vui lòng thử lại.", "AI_REVIEW_UNAVAILABLE");
        var result = JsonSerializer.Deserialize<DeliverableReviewResponseDto>(row.ResultJson ?? "{}") ?? throw new ConflictException("AI review result is unavailable.");
        await Read(projectId, result.DeliverableVersionId, ct);
        return result;
    }
    private static string FileSignature(AIPMS.Infrastructure.Persistence.Generated.Models.DeliverableVersion v) =>
        JsonSerializer.Serialize(new { v.Id, v.DeliverableId, v.Deliverable.EvidenceScope, v.Deliverable.TargetMajorId, v.Deliverable.TargetStudentId,
            files = v.Files.OrderBy(f => f.Id).Select(f => new { f.Id, f.StoragePath, f.ChecksumSha256, f.FileSizeBytes }) });
}
