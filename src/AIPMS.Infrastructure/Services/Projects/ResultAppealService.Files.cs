using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Appeals;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ResultAppealService
{
    private async Task UploadGuard(ResultAppealRequest row, Access access, string token, CancellationToken ct)
    {
        await Read(row, access, ct); Token(row.ConcurrencyToken, token); await Current(row, ct);
        if (row.RequestedBy != Actor || row.Status != "PENDING") throw new ForbiddenException("Only the requester may attach evidence before review starts.");
        if (row.Source == "STUDENT_APPEAL")
        {
            var window = await db.Set<ResultAppealWindow>().SingleAsync(w => w.Id == row.WindowId, ct);
            if (window.Status != "OPEN" || Now < window.OpensAt || Now >= window.ClosesAt) throw new ConflictException("Appeal upload window closed.", "APPEAL_WINDOW_CLOSED");
        }
    }
    public async Task<CorrectionRequestDto> Upload(long projectId, long id, string token, UploadContent content, CancellationToken ct)
    {
        var access = await Scope(projectId, ct); await UploadGuard(await Row(projectId, id, ct), access, token, ct);
        if (content.Length is <= 0 or > 10 * 1024 * 1024) throw Invalid("Appeal evidence is limited to 10 MiB per file.");
        var upload = await UploadValidator.ReadAsync(content, ct); db.ChangeTracker.Clear(); effective = null;
        string? key = null;
        return await files.InTransactionAsync(async transactionCt => {
            await transactions.LockProjectAsync(projectId, transactionCt);
            var freshAccess = await Scope(projectId, transactionCt); var row = await Row(projectId, id, transactionCt);
            await UploadGuard(row, freshAccess, token, transactionCt);
            if (row.Files.Count >= 10) throw Invalid("At most ten evidence files per appeal.");
            var newKey = Guid.NewGuid().ToString("N");
            using var stream = new MemoryStream(upload.Bytes, writable: false);
            await storage.WriteAsync(newKey, stream, transactionCt); key = newKey;
            var file = new M.File { UploadedBy = Actor, OriginalFileName = upload.FileName, MimeType = upload.ContentType,
                FileSizeBytes = upload.Bytes.Length, ChecksumSha256 = upload.Sha256, StoragePath = newKey, CreatedAt = Now, UpdatedAt = Now };
            db.Add(file); await db.SaveChangesAsync(transactionCt);
            row.Files.Add(new() { FileId = file.Id, FileName = upload.FileName, ContentType = upload.ContentType,
                SizeBytes = upload.Bytes.Length, Sha256 = upload.Sha256, StorageKey = newKey });
            row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(transactionCt); await Event(row, "APPEAL_FILE_ATTACHED", null, transactionCt);
            return await Dto(row, freshAccess, transactionCt);
        }, ct, async () => {
            if (key is not null) try { await storage.DeleteAsync(key, CancellationToken.None); }
            catch (Exception ex) { logger.LogError(ex, "Appeal upload cleanup failed; storage reconciliation required"); }
        });
    }
    private async Task<FileDownload> Open(AppealFileDto file, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(file.Sha256)) throw new ConflictException("Exact evidence reference missing.");
        try { return new(await storage.OpenReadAsync(key, ct), file.ContentType, file.FileName); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new NotFoundException("EvidenceContent", file.FileId); }
    }
    public async Task<FileDownload> Download(long projectId, long id, long fileId, CancellationToken ct)
    {
        var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct); await Read(row, access, ct);
        var file = row.Files.SingleOrDefault(f => f.FileId == fileId) ?? throw new NotFoundException("AppealFile", fileId);
        return await Open(new(file.FileId, file.FileName, file.ContentType, file.SizeBytes, file.Sha256, ""), file.StorageKey, ct);
    }
    private sealed record EvidenceFile(AppealFileDto Dto, string Key);
    private async Task<EvidenceFile[]> LockedEvidence(ResultAppealRequest row, Access access, CancellationToken ct)
    {
        await Read(row, access, ct);
        // A student's request does not grant access to other students' shared/Common evidence.
        if (!DepartmentAccess(row, access))
        {
            if (row.ReviewerId != Actor) throw new ForbiddenException("Locked scoring evidence is restricted to the assigned reviewer and scoped staff.");
            await Reviewer(row, Actor, ct);
        }
        var frozen = Frozen(row);
        using var root = JsonDocument.Parse(row.FrozenInputs);
        var packageId = root.RootElement.GetProperty("PackageId").GetInt64();
        string Url(long fileId) => $"/api/v1/projects/{row.ProjectId}/result/correction-requests/{row.Id}/evidence/{fileId}";
        if (frozen.Component.Stage == "COLD")
        {
            var data = await ColdEvaluationBinding.Load(db, row.ProjectId, ct);
            var binding = ColdEvaluationBinding.Capture(data, row.Scope, row.MajorId, row.Scope == "INDIVIDUAL" ? row.StudentId : null);
            if (frozen.Evaluations.Any(e => e.Finalization?.Evidence.ColdPackageId != binding.PackageId
                || e.Finalization.Evidence.ColdEvidenceHash != binding.Hash
                || !(e.Finalization.Evidence.ColdSubmissionIds ?? []).SequenceEqual(binding.SubmissionIds))) throw new ConflictException("Locked COLD evidence differs from the result snapshot.");
            return data.Submissions.Where(s => binding.SubmissionIds.Contains(s.Id)).SelectMany(s => s.Files.Select(f =>
                new EvidenceFile(new(f.FileId, f.FileName, f.ContentType, f.SizeBytes, f.Sha256, Url(f.FileId)), f.StorageKey))).ToArray();
        }
        var final = await db.Set<FinalSubmission>().AsNoTracking().Include(f => f.Items)
            .SingleOrDefaultAsync(f => f.ProjectId == row.ProjectId && f.Id == packageId, ct) ?? throw new ConflictException("Exact locked final package missing.");
        var versions = frozen.Evaluations.SelectMany(e => e.Finalization!.Evidence.DeliverableVersionIds).Distinct().ToHashSet();
        return final.Items.Where(i => versions.Contains(i.DeliverableVersionId)).SelectMany(i =>
            (JsonSerializer.Deserialize<FinalSnapshotFile[]>(i.FilesJson) ?? []).Where(f => f.Metadata.ParentId == i.DeliverableVersionId
                && f.Metadata.ParentType is "VERSION" or "DELIVERABLE_VERSION"
                && (row.Scope == "COMMON" || f.EvidenceScope is "MAJOR_SPECIFIC" or "INDIVIDUAL" && f.TargetMajorId == row.MajorId
                    && (row.Scope != "INDIVIDUAL" || f.EvidenceScope == "INDIVIDUAL" && f.TargetStudentId == row.StudentId)))
            .Select(f => new EvidenceFile(new(f.Metadata.Id, f.Metadata.FileName, f.Metadata.ContentType, f.Metadata.SizeBytes,
                f.Metadata.Sha256, Url(f.Metadata.Id)), f.StorageKey))).ToArray();
    }
    public async Task<IReadOnlyList<AppealFileDto>> Evidence(long projectId, long id, CancellationToken ct)
    {
        var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct);
        return (await LockedEvidence(row, access, ct)).Select(f => f.Dto).ToArray();
    }
    public async Task<FileDownload> DownloadEvidence(long projectId, long id, long fileId, CancellationToken ct)
    {
        var access = await Scope(projectId, ct); var row = await Row(projectId, id, ct);
        var file = (await LockedEvidence(row, access, ct)).SingleOrDefault(f => f.Dto.FileId == fileId) ?? throw new NotFoundException("LockedEvidence", fileId);
        return await Open(file.Dto, file.Key, ct);
    }
}
