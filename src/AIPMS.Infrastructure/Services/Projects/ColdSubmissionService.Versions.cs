using System.Security.Cryptography;
using System.IO;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ColdSubmissionService
{
    public Task<ColdSubmissionDto> SaveDraft(long itemId, SaveColdDraftRequest input, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var projectId = await (from target in db.Set<ColdChecklistItem>() join pack in db.Set<ColdPackage>() on target.PackageId equals pack.Id
            where target.Id == itemId select (long?)pack.ProjectId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("ColdChecklistItem", itemId);
        await evaluations.LockProjectAsync(projectId, ct);
        var (item, package) = await Item(itemId, ct);
        var access = await Scope(projectId, package, ct);
        if (!access.Writes(item)) throw new ForbiddenException("Students can submit only for their frozen major or own individual target.");
        await WriteWindow(package, access, ct);
        if (input.ExpectedLatestVersion < 0 || input.Note?.Length > 2000) throw Invalid("Invalid expected version or note longer than 2000 characters.");
        var latest = await db.Set<ColdSubmission>().Include(s => s.Files).Where(s => s.ChecklistItemId == item.Id).OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        if ((latest?.Version ?? 0) != input.ExpectedLatestVersion) throw new ConflictException("A newer COLD version exists.");
        ColdSubmission row;
        if (latest?.Status == "DRAFT") { Token(latest.ConcurrencyToken, input.ConcurrencyToken); row = latest; }
        else
        {
            if (input.ConcurrencyToken is not null) throw new ConflictException("Create a new version without the old draft token.");
            row = new() { ChecklistItemId = item.Id, Version = (latest?.Version ?? 0) + 1, CreatedBy = Actor, CreatedAt = Now };
            db.Add(row);
        }
        row.Note = input.Note?.Trim(); row.ConcurrencyToken = Guid.NewGuid(); package.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Audit("COLD_DRAFT_SAVED", "COLD_SUBMISSION", row.Id, Dto(row, item, package), ct);
        return Dto(row, item, package);
    }, ct);

    private async Task<(ColdSubmission Row, ColdChecklistItem Item, ColdPackage Package, Access Access)> Writable(long id, string token, CancellationToken ct)
    {
        var projectId = await (from s in db.Set<ColdSubmission>() join i in db.Set<ColdChecklistItem>() on s.ChecklistItemId equals i.Id
            join p in db.Set<ColdPackage>() on i.PackageId equals p.Id where s.Id == id select (long?)p.ProjectId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ColdSubmission", id);
        await evaluations.LockProjectAsync(projectId, ct);
        var (row, item, package) = await Submission(id, ct);
        var access = await Scope(projectId, package, ct);
        if (!access.Writes(item)) throw new ForbiddenException();
        await WriteWindow(package, access, ct);
        Token(row.ConcurrencyToken, token);
        if (row.Status != "DRAFT") throw new ConflictException("Submitted COLD versions are immutable; create a new draft version.");
        return (row, item, package, access);
    }
    public async Task<ColdSubmissionDto> Upload(long id, string token, UploadContent content, CancellationToken ct)
    {
        var initial = await Submission(id, ct);
        var access = await Scope(initial.Package.ProjectId, initial.Package, ct);
        if (!access.Writes(initial.Item)) throw new ForbiddenException();
        await WriteWindow(initial.Package, access, ct);
        Token(initial.Row.ConcurrencyToken, token);
        if (content.Length is <= 0 or > 10 * 1024 * 1024 || Path.GetExtension(content.FileName).ToLowerInvariant() is not (".pdf" or ".docx" or ".zip"))
            throw Invalid("COLD accepts PDF/DOCX/ZIP only, maximum 10 MiB per file.");
        var upload = await UploadValidator.ReadAsync(content, ct);
        if (upload.Bytes.Length > 10 * 1024 * 1024) throw Invalid("COLD file exceeds 10 MiB.");
        db.ChangeTracker.Clear();
        string? storedKey = null;
        return await files.InTransactionAsync(async transactionCt =>
        {
            var (row, item, package, _) = await Writable(id, token, transactionCt);
            if (row.Files.Count >= 10) throw Invalid("At most ten files per report version.");
            var key = Guid.NewGuid().ToString("N");
            using var stream = new MemoryStream(upload.Bytes, writable: false);
            await storage.WriteAsync(key, stream, transactionCt); storedKey = key;
            var file = new M.File { UploadedBy = Actor, OriginalFileName = upload.FileName, MimeType = upload.ContentType,
                FileSizeBytes = upload.Bytes.Length, ChecksumSha256 = upload.Sha256, StoragePath = key, CreatedAt = Now, UpdatedAt = Now };
            db.Files.Add(file); await db.SaveChangesAsync(transactionCt);
            row.Files.Add(new() { FileId = file.Id, FileName = upload.FileName, ContentType = upload.ContentType,
                SizeBytes = upload.Bytes.Length, Sha256 = upload.Sha256, StorageKey = key });
            row.ConcurrencyToken = Guid.NewGuid(); package.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(transactionCt); await Audit("COLD_FILE_ATTACHED", "COLD_SUBMISSION", row.Id, Dto(row, item, package), transactionCt);
            return Dto(row, item, package);
        }, ct, () => Cleanup(storedKey));
    }
    private async Task Cleanup(string? key)
    {
        if (key is null) return;
        try { await storage.DeleteAsync(key, CancellationToken.None); }
        catch (Exception ex) { logger.LogError(ex, "COLD private upload cleanup failed; storage reconciliation required"); }
    }
    public async Task<ColdSubmissionDto> RemoveFile(long id, long fileId, string token, CancellationToken ct)
    {
        string? removedKey = null;
        var result = await evaluations.InTransactionAsync(async () =>
        {
            var (row, item, package, _) = await Writable(id, token, ct);
            var file = row.Files.SingleOrDefault(f => f.FileId == fileId) ?? throw new NotFoundException("ColdFile", fileId);
            removedKey = file.StorageKey;
            db.Remove(file); row.Files.Remove(file);
            var metadata = await db.Files.SingleAsync(f => f.Id == fileId, ct);
            db.Remove(metadata); row.ConcurrencyToken = Guid.NewGuid(); package.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct); await Audit("COLD_FILE_REMOVED", "COLD_SUBMISSION", row.Id, new { FileId = fileId, row.Version }, ct);
            return Dto(row, item, package);
        }, ct);
        await Cleanup(removedKey); return result;
    }
    private async Task ValidateContent(ColdSubmissionFile file, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(file.StorageKey, ct);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var bytes = new byte[81920]; long total = 0; int count;
            while ((count = await stream.ReadAsync(bytes, ct)) > 0)
            {
                total += count;
                if (total > file.SizeBytes || total > 10 * 1024 * 1024) throw new ConflictException("COLD file content changed.");
                hash.AppendData(bytes, 0, count);
            }
            if (total != file.SizeBytes || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("COLD file content changed.");
        }
        catch (IOException) { throw new ConflictException("COLD file content is unavailable."); }
    }
    public Task<ColdSubmissionDto> Submit(long id, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var (row, item, package, access) = await Writable(id, token, ct);
        if (row.Files.Count == 0) throw new ConflictException("A report requires at least one valid file.");
        foreach (var file in row.Files) await ValidateContent(file, ct);
        await WriteWindow(package, access, ct);
        row.Status = "SUBMITTED"; row.SubmittedAt = Now; row.ConcurrencyToken = Guid.NewGuid(); package.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Audit("COLD_SUBMITTED", "COLD_SUBMISSION", row.Id, Dto(row, item, package), ct);
        await WriteWindow(package, access, ct);
        return Dto(row, item, package);
    }, ct);
    public Task<ColdChecklistDto> Lock(long projectId, string token, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        await evaluations.LockProjectAsync(projectId, ct);
        var package = await Package(projectId, ct);
        var access = await Scope(projectId, package, ct);
        if (!access.Lead) throw new ForbiddenException("Only the frozen lead department locks COLD evidence.");
        Token(package.ConcurrencyToken, token);
        var checklist = await ChecklistDto(package, access, ct);
        if (!checklist.CanLock) throw new ConflictException(string.Join(", ", checklist.Blockers));
        var ids = checklist.Items.Where(i => i.Latest is not null).Select(i => i.Latest!.Id).ToArray();
        var rows = await db.Set<ColdSubmission>().Include(s => s.Files).Where(s => ids.Contains(s.Id)).ToArrayAsync(ct);
        foreach (var row in rows)
        {
            foreach (var file in row.Files) await ValidateContent(file, ct);
            row.Status = "LOCKED"; row.LockedAt = Now; row.ConcurrencyToken = Guid.NewGuid();
        }
        package.Status = "LOCKED"; package.LockedBy = Actor; package.LockedAt = Now; package.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Audit("COLD_PACKAGE_LOCKED", "COLD_PACKAGE", package.Id,
            new { package.ProjectId, package.Deadline, Versions = rows.Select(r => new { r.Id, r.ChecklistItemId, r.Version }) }, ct);
        return await ChecklistDto(package, access, ct);
    }, ct);
    public Task<PagedResult<ColdSubmissionDto>> History(long itemId, int page, int pageSize, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        Page(page, pageSize); var (item, package) = await Item(itemId, ct);
        var access = await Scope(package.ProjectId, package, ct);
        if (!access.Reads(item)) throw new ForbiddenException();
        var query = db.Set<ColdSubmission>().AsNoTracking().Where(s => s.ChecklistItemId == itemId);
        var count = await query.LongCountAsync(ct);
        var rows = await query.Include(s => s.Files).OrderByDescending(s => s.Version).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new PagedResult<ColdSubmissionDto>(rows.Select(r => Dto(r, item, package)).ToArray(), page, pageSize, count);
    }, ct);
    public Task<FileDownload> Download(long id, long fileId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var (row, item, package) = await Submission(id, ct);
        var access = await Scope(package.ProjectId, package, ct);
        if (!access.Reads(item)) throw new ForbiddenException();
        var file = row.Files.SingleOrDefault(f => f.FileId == fileId) ?? throw new NotFoundException("ColdFile", fileId);
        try { return new FileDownload(await storage.OpenReadAsync(file.StorageKey, ct), file.ContentType, file.FileName); }
        catch (IOException) { throw new NotFoundException("ColdFileContent", fileId); }
    }, ct);
}
