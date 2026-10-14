using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Application.Features.Industry;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class IndustryService
{
    private static IndustryFileDto FileDto(IndustryEvidenceShare f) => new(f.Id, f.FileId, f.DeliverableVersionId,
        f.FileName, f.ContentType, f.SizeBytes, f.Sha256, $"/api/v1/industry/assignments/{f.AssignmentId}/evidence/{f.Id}/download");
    private static bool FileScope(IndustryAssignment row, string? scope, long? major, long? student) => student is null
        && (row.Scope == "COMMON" ? scope == "COMMON" && major is null : scope == "MAJOR_SPECIFIC" && major == row.MajorId);
    public Task<IndustryFileDto> Share(long id, ShareIndustryFileRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        var projectId = (await Row(id, ct)).ProjectId; db.ChangeTracker.Clear(); await transactions.LockProjectAsync(projectId, ct);
        Writable(await Lead(projectId, ct)); var row = await Row(id, ct); Token(row, input.ConcurrencyToken);
        if (!Live(row)) throw new ConflictException("An active assignment is required.");
        var file = await db.Files.AsNoTracking().Include(f => f.DeliverableVersion!).ThenInclude(v => v.Deliverable)
            .SingleOrDefaultAsync(f => f.Id == input.FileId && f.DeliverableVersionId == input.DeliverableVersionId
                && f.DeliverableVersion!.Deliverable.ProjectId == projectId, ct) ?? throw new NotFoundException("ProjectVersionFile", input.FileId);
        var share = new IndustryEvidenceShare { AssignmentId = id, DeliverableVersionId = input.DeliverableVersionId, FileId = input.FileId,
            SharedBy = Actor, SharedAt = Now };
        var locked = await db.Set<FinalSubmissionItem>().AsNoTracking().Where(i => db.Set<FinalSubmission>().Any(s => s.Id == i.SubmissionId && s.ProjectId == projectId)
            && i.DeliverableVersionId == input.DeliverableVersionId).SingleOrDefaultAsync(ct);
        if (locked is not null)
        {
            FinalSnapshotFile? exact;
            try { exact = (JsonSerializer.Deserialize<FinalSnapshotFile[]>(locked.FilesJson) ?? []).SingleOrDefault(f => f.Metadata.Id == input.FileId
                && f.Metadata.ParentId == input.DeliverableVersionId && f.Metadata.ParentType is "VERSION" or "DELIVERABLE_VERSION"); }
            catch (JsonException) { throw new ConflictException("Locked file provenance is unknown."); }
            if (exact is null || !FileScope(row, exact.EvidenceScope, exact.TargetMajorId, exact.TargetStudentId)) throw new ForbiddenException("File outside advisory target.");
            share.FileName = exact.Metadata.FileName; share.ContentType = exact.Metadata.ContentType; share.SizeBytes = exact.Metadata.SizeBytes;
            share.Sha256 = exact.Metadata.Sha256 ?? ""; share.StorageKey = exact.StorageKey;
        }
        else
        {
            var artifact = file.DeliverableVersion!.Deliverable;
            if (!FileScope(row, artifact.EvidenceScope, artifact.TargetMajorId, artifact.TargetStudentId)) throw new ForbiddenException("File outside advisory target.");
            share.FileName = file.OriginalFileName; share.ContentType = file.MimeType ?? "application/octet-stream"; share.SizeBytes = file.FileSizeBytes;
            share.Sha256 = file.ChecksumSha256 ?? ""; share.StorageKey = file.StoragePath;
        }
        if (share.Sha256.Length != 64 || string.IsNullOrWhiteSpace(share.StorageKey) || share.SizeBytes is <= 0 or > 10*1024*1024)
            throw new ConflictException("Exact file provenance and at most 10 MiB required.");
        if (await db.Set<IndustryEvidenceShare>().AnyAsync(f => f.AssignmentId == id && f.FileId == input.FileId && f.RevokedAt == null, ct)) throw new ConflictException("File already shared.");
        db.Add(share); row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await Audit("INDUSTRY_FILE_SHARED", "INDUSTRY_SHARE", share.Id, projectId, ct); return FileDto(share);
    }, ct);
    public Task<IndustryAssignmentDto> Unshare(long id, long shareId, string token, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        var projectId = (await Row(id, ct)).ProjectId; db.ChangeTracker.Clear(); await transactions.LockProjectAsync(projectId, ct);
        await Lead(projectId, ct); var row = await Row(id, ct); Token(row, token);
        var share = await db.Set<IndustryEvidenceShare>().SingleOrDefaultAsync(f => f.Id == shareId && f.AssignmentId == id && f.RevokedAt == null, ct)
            ?? throw new NotFoundException("IndustryShare", shareId);
        share.RevokedAt = Now; row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await Audit("INDUSTRY_FILE_REVOKED", "INDUSTRY_SHARE", shareId, projectId, ct); return await Dto(row, true, ct);
    }, ct);
    public async Task<PagedResult<IndustryFileDto>> Evidence(long id, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await Read(id, ct);
        var query = db.Set<IndustryEvidenceShare>().AsNoTracking().Where(f => f.AssignmentId == id && f.RevokedAt == null);
        var count = await query.CountAsync(ct); var rows = await query.OrderBy(f => f.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(FileDto).ToArray(), page, pageSize, count);
    }
    public async Task<FileDownload> Download(long id, long shareId, CancellationToken ct)
    {
        await Read(id, ct);
        var share = await db.Set<IndustryEvidenceShare>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == shareId && f.AssignmentId == id && f.RevokedAt == null, ct)
            ?? throw new NotFoundException("IndustryShare", shareId);
        try { return new(await storage.OpenReadAsync(share.StorageKey, ct), share.ContentType, share.FileName); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new NotFoundException("SharedFileContent", shareId); }
    }
    private static IndustryFeedbackDto FeedbackDto(IndustryFeedback row) => new(row.Id, row.Version, row.Version == 1 ? null : row.Version-1,
        row.Content, row.SubmittedBy, Utc(row.SubmittedAt));
    public Task<IndustryFeedbackDto> Feedback(long id, IndustryFeedbackRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        var projectId = (await Row(id, ct)).ProjectId; db.ChangeTracker.Clear(); await transactions.LockProjectAsync(projectId, ct);
        var access = await Read(id, ct); var row = access.Row; Token(row, input.ConcurrencyToken);
        if (access.Lead || row.ExpertId != Actor) throw new ForbiddenException("Only the assigned expert may submit advisory feedback.");
        Writable(await transactions.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId));
        if (string.IsNullOrWhiteSpace(input.Content) || input.Content.Length > 10000) throw Invalid("Feedback must contain 1..10000 characters.");
        var version = await db.Set<IndustryFeedback>().Where(f => f.AssignmentId == id).Select(f => (int?)f.Version).MaxAsync(ct) ?? 0;
        if (version != input.ExpectedVersion) throw new ConflictException("Feedback version changed.", "INDUSTRY_STALE");
        var feedback = new IndustryFeedback { AssignmentId = id, Version = version+1, Content = input.Content.Trim(), SubmittedBy = Actor, SubmittedAt = Now };
        db.Add(feedback); row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await Audit("INDUSTRY_FEEDBACK_SUBMITTED", "INDUSTRY_FEEDBACK", feedback.Id, projectId, ct); return FeedbackDto(feedback);
    }, ct);
    public async Task<PagedResult<IndustryFeedbackDto>> FeedbackHistory(long id, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await Read(id, ct);
        var query = db.Set<IndustryFeedback>().AsNoTracking().Where(f => f.AssignmentId == id); var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(f => f.Version).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(FeedbackDto).ToArray(), page, pageSize, count);
    }
}
