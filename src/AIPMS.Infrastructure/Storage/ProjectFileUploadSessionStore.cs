using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Files;
using AIPMS.Application.Features.Files.Abstractions;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Storage;

internal sealed class ProjectFileUploadSessionStore : IProjectFileUploadSessions
{
    internal sealed record Entry(string SessionId, long ActorId, string ParentType, long ParentId,
        string FileName, string ContentType, long TotalBytes, long NextOffset, DateTime ExpiresAt,
        string State = "UPLOADING", ProjectFileDto? File = null, string? Error = null)
    {
        public ProjectFileUploadSession Public => new(SessionId, ParentType, ParentId, FileName,
            TotalBytes, NextOffset, (int)UploadPolicies.ProjectFileChunkSize, ExpiresAt, State, File, Error);
    }

    private readonly string root;
    private readonly long maxReservedBytes;
    private readonly TimeProvider clock;
    private const long DiskHeadroom = 64L * 1024 * 1024;
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public ProjectFileUploadSessionStore(IConfiguration configuration, TimeProvider clock)
    {
        var storageRoot = configuration["FileStorage:RootPath"];
        root = Path.GetFullPath(configuration["FileStorage:UploadSessionsPath"] ?? Path.Combine(
            string.IsNullOrWhiteSpace(storageRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIPMS", "private-files")
                : storageRoot, ".upload-sessions"));
        maxReservedBytes = long.TryParse(configuration["FileStorage:UploadSessionQuotaBytes"], out var quota)
            && quota >= UploadPolicies.ProjectFileLimit ? quota : 8L * 1024 * 1024 * 1024;
        this.clock = clock;
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string PathFor(string id, string suffix) => Path.Combine(root, id + suffix);
    private static void CheckId(string id)
    {
        if (id.Length != 32 || id.Any(c => !char.IsAsciiHexDigit(c)) || id != id.ToLowerInvariant())
            throw new NotFoundException("Phiên tải tệp", id);
    }

    internal async Task<FileStream> LockAsync(string id, string suffix, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            // Fixed lock stripes avoid leaving one lock file per expired or unknown session.
            var lockKey = id == "quota" ? id : "gate-" + id[..2];
            try { return new FileStream(PathFor(lockKey, suffix), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33) { await Task.Delay(40, ct); }
        }
    }

    private async Task<Entry> ReadAsync(string id, CancellationToken ct)
    {
        try
        {
            if (new FileInfo(PathFor(id, ".json")).Length > 16 * 1024) throw new JsonException("Upload metadata is too large.");
            await using var stream = new FileStream(PathFor(id, ".json"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var entry = await JsonSerializer.DeserializeAsync<Entry>(stream, cancellationToken: ct)
                ?? throw new JsonException("Invalid upload session metadata.");
            if (entry.SessionId != id || id.Length != 32 || id.Any(c => !char.IsAsciiHexDigit(c)) || id != id.ToLowerInvariant()
                || entry.ActorId <= 0 || entry.ParentId <= 0 || entry.ParentType is not ("TASK" or "REPORT" or "MEETING")
                || entry.TotalBytes <= 0 || entry.TotalBytes > UploadPolicies.ProjectFileLimit
                || entry.NextOffset < 0 || entry.NextOffset > entry.TotalBytes
                || string.IsNullOrWhiteSpace(entry.FileName) || entry.FileName.Length > 255 || !MatchesMime(Path.GetExtension(entry.FileName).ToLowerInvariant(), entry.ContentType)
                || entry.State is not ("UPLOADING" or "PROCESSING" or "COMPLETED" or "FAILED" or "CANCELLED")
                || entry.State == "COMPLETED" && entry.File is null)
                throw new JsonException("Upload session metadata failed integrity checks.");
            return entry;
        }
        catch (FileNotFoundException) { throw new NotFoundException("Phiên tải tệp", id); }
    }

    private async Task SaveAsync(Entry entry, CancellationToken ct)
    {
        var temporary = PathFor(entry.SessionId, ".json.tmp");
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, entry, cancellationToken: ct);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }
        System.IO.File.Move(temporary, PathFor(entry.SessionId, ".json"), overwrite: true);
    }

    private async Task<Entry> OwnedAsync(long actorId, string id, CancellationToken ct)
    {
        var entry = await ReadAsync(id, ct);
        if (entry.ActorId != actorId || entry.ExpiresAt <= Now || entry.State == "CANCELLED") throw new NotFoundException("Phiên tải tệp", id);
        return entry;
    }

    public async Task<ProjectFileUploadSession> CreateAsync(long actorId, CreateProjectFileUploadSession request, CancellationToken ct)
    {
        if (actorId <= 0 || request.ParentId <= 0 || request.ParentType is not ("TASK" or "REPORT" or "MEETING"))
            throw Invalid("Thông tin tệp hoặc hạng mục không hợp lệ.");
        ProjectFileUploadValidator.ValidateMetadata(request.FileName ?? "", request.ContentType ?? "", request.SizeBytes);
        var name = request.FileName!.Trim();

        await using var quotaLock = await LockAsync("quota", ".lock", ct);
        var entries = await EntriesAsync(ct);
        var active = entries.Where(x => x.State is not ("COMPLETED" or "CANCELLED")).ToArray();
        if (active.Count(x => x.ActorId == actorId) >= 2)
            throw new ConflictException("Bạn đang có 2 phiên tải tệp. Hãy hoàn tất hoặc hủy một phiên trước.");
        var quarantined = Directory.EnumerateFiles(root, "*.quarantined").ToArray();
        var quarantinedBytes = quarantined.Sum(path =>
        {
            var content = new FileInfo(PathFor(Path.GetFileNameWithoutExtension(path), ".part"));
            return content.Exists ? content.Length : 0;
        });
        if (entries.Count + quarantined.Length >= 10000 || active.Sum(x => x.TotalBytes) + quarantinedBytes > maxReservedBytes - request.SizeBytes)
            throw new ConflictException("Máy chủ đang tiếp nhận nhiều tệp lớn. Vui lòng thử lại sau.");
        var disk = new DriveInfo(Path.GetPathRoot(root)!);
        if (disk.AvailableFreeSpace < 2 * request.SizeBytes + active.Sum(x => 2 * x.TotalBytes - x.NextOffset) + DiskHeadroom)
            throw new ConflictException("Máy chủ chưa đủ dung lượng trống để tiếp nhận tệp này.");
        var entry = new Entry(Guid.NewGuid().ToString("N"), actorId, request.ParentType, request.ParentId,
            name, request.ContentType!, request.SizeBytes, 0, Now.Add(Lifetime));
        await using (var content = new FileStream(PathFor(entry.SessionId, ".part"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try { await SaveAsync(entry, ct); }
        catch { System.IO.File.Delete(PathFor(entry.SessionId, ".part")); throw; }
        return entry.Public;
    }

    private static bool MatchesMime(string extension, string mime) => extension switch
    {
        ".pdf" => mime == "application/pdf",
        ".docx" => mime == "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".zip" => mime == "application/zip",
        ".png" => mime == "image/png",
        ".jpg" or ".jpeg" => mime == "image/jpeg",
        _ => false
    };

    public async Task<ProjectFileUploadSession> GetAsync(long actorId, string sessionId, CancellationToken ct)
    {
        CheckId(sessionId);
        await using var gate = await LockAsync(sessionId, ".lock", ct);
        return (await OwnedAsync(actorId, sessionId, ct)).Public;
    }

    public async Task<ProjectFileUploadSession> AppendAsync(long actorId, string sessionId, long offset,
        Stream content, long length, CancellationToken ct)
    {
        CheckId(sessionId);
        if (length > UploadPolicies.ProjectFileChunkSize)
            throw new PayloadTooLargeException(UploadPolicies.ProjectFileChunkSize, "Mỗi phần tệp chỉ được tối đa 4 MiB.");
        if (length <= 0 || offset < 0) throw Invalid("Kích thước hoặc vị trí phần tệp không hợp lệ.");
        await using var gate = await LockAsync(sessionId, ".lock", ct);
        var entry = await OwnedAsync(actorId, sessionId, ct);
        if (entry.State != "UPLOADING") throw new ConflictException("Phiên tải tệp không còn nhận dữ liệu.");
        if (offset > entry.NextOffset || offset > entry.TotalBytes - length)
            throw new ConflictException("Vị trí tải tệp đã thay đổi. Hãy đọc lại tiến độ tải.");
        await using var output = new FileStream(PathFor(sessionId, ".part"), FileMode.Open, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.Asynchronous);
        if (output.Length < entry.NextOffset) throw new IOException("Upload data is shorter than its durable offset.");
        // A crash between the data flush and metadata replacement can leave an unacknowledged tail.
        output.SetLength(entry.NextOffset);
        output.Position = offset;
        var duplicate = offset < entry.NextOffset;
        if (duplicate && length > entry.NextOffset - offset)
            throw new ConflictException("Phần tệp chồng lấn dữ liệu đã nhận. Hãy đọc lại tiến độ tải.");
        var buffer = new byte[81920];
        var previous = duplicate ? new byte[buffer.Length] : null;
        long received = 0;
        try
        {
            while (received < length)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - received)), ct);
                if (read == 0) throw Invalid("Phần tệp chưa được truyền đủ. Hãy thử lại.");
                if (duplicate)
                {
                    await output.ReadExactlyAsync(previous!.AsMemory(0, read), ct);
                    if (!buffer.AsSpan(0, read).SequenceEqual(previous.AsSpan(0, read)))
                        throw new ConflictException("Nội dung phần tệp khác dữ liệu đã nhận. Hãy bắt đầu phiên tải mới.");
                }
                else await output.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
            }
            if (await content.ReadAsync(buffer.AsMemory(0, 1), ct) != 0)
                throw Invalid("Kích thước phần tệp không khớp dữ liệu được gửi.");
            if (duplicate) return entry.Public;
            await output.FlushAsync(ct);
            output.Flush(flushToDisk: true);
            entry = entry with { NextOffset = offset + length };
            await SaveAsync(entry, ct);
            return entry.Public;
        }
        catch
        {
            // Metadata remains authoritative if a write is interrupted.
            var durable = await ReadAsync(sessionId, CancellationToken.None);
            output.SetLength(durable.NextOffset);
            throw;
        }
    }

    public async Task<ProjectFileUploadSession> CompleteAsync(long actorId, string sessionId, CancellationToken ct)
    {
        CheckId(sessionId);
        await using var gate = await LockAsync(sessionId, ".lock", ct);
        var entry = await OwnedAsync(actorId, sessionId, ct);
        if (entry.State is "COMPLETED" or "PROCESSING") return entry.Public;
        if (entry.NextOffset != entry.TotalBytes) throw new ConflictException("Tệp chưa được tải lên đầy đủ.");
        entry = entry with { State = "PROCESSING", Error = null };
        await SaveAsync(entry, ct);
        return entry.Public;
    }

    public async Task CancelAsync(long actorId, string sessionId, CancellationToken ct)
    {
        CheckId(sessionId);
        await using var gate = await LockAsync(sessionId, ".lock", ct);
        var entry = await OwnedAsync(actorId, sessionId, ct);
        if (entry.State is "PROCESSING" or "COMPLETED")
            throw new ConflictException("Tệp đang được lưu hoặc đã lưu thành công nên không thể hủy phiên tải.");
        await SaveAsync(entry with { State = "CANCELLED", Error = null }, ct);
        System.IO.File.Delete(PathFor(sessionId, ".part"));
    }

    private void DeleteData(string id)
    {
        System.IO.File.Delete(PathFor(id, ".part"));
        System.IO.File.Delete(PathFor(id, ".json"));
        System.IO.File.Delete(PathFor(id, ".json.tmp"));
    }

    internal async Task<List<Entry>> EntriesAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var entries = new List<Entry>();
        foreach (var file in Directory.EnumerateFiles(root, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            try { entries.Add(await ReadAsync(Path.GetFileNameWithoutExtension(file), ct)); }
            catch (NotFoundException) { }
            catch (JsonException)
            {
                // Isolate damaged metadata so unrelated uploads continue. Its content is retained for reconciliation.
                var id = Path.GetFileNameWithoutExtension(file);
                if (id.Length == 32 && id.All(char.IsAsciiHexDigit) && id == id.ToLowerInvariant())
                {
                    await using var gate = await LockAsync(id, ".lock", ct);
                    try { await ReadAsync(id, ct); }
                    catch (JsonException) { System.IO.File.Move(file, PathFor(id, ".quarantined"), overwrite: true); }
                    catch (NotFoundException) { }
                }
            }
        }
        return entries;
    }

    internal async Task ProcessAsync(string sessionId, Func<Entry, Stream, CancellationToken, Task<ProjectFileDto>> process, CancellationToken ct)
    {
        // Separate process and metadata locks allow polling while a slow storage upload runs.
        await using var processing = await LockAsync(sessionId, ".processing", ct);
        Entry entry;
        await using (var gate = await LockAsync(sessionId, ".lock", ct))
        {
            try { entry = await ReadAsync(sessionId, ct); }
            catch (NotFoundException) { return; }
            if (entry.State != "PROCESSING") return;
            if (entry.ExpiresAt <= Now) return;
        }
        try
        {
            ProjectFileDto file;
            await using (var content = new FileStream(PathFor(sessionId, ".part"), FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (content.Length != entry.TotalBytes) throw Invalid("Kích thước tệp chưa đầy đủ.");
                file = await process(entry, content, ct);
            }
            await using var gate = await LockAsync(sessionId, ".lock", ct);
            await SaveAsync(entry with { State = "COMPLETED", File = file, Error = null, ExpiresAt = Now.Add(Lifetime) }, ct);
            System.IO.File.Delete(PathFor(sessionId, ".part"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            await using var gate = await LockAsync(sessionId, ".lock", CancellationToken.None);
            if ((await ReadAsync(sessionId, CancellationToken.None)).State == "COMPLETED") return;
            var error = ex is AIPMS.Domain.Exceptions.DomainException domainEx && domainEx.Code == WorkflowErrorCodes.FileQuarantined
                ? $"{WorkflowErrorCodes.FileQuarantined}: Tệp không vượt qua bước quét an toàn và đã bị cách ly."
                : ex is ValidationException
                    ? "Tệp không đúng định dạng hoặc nội dung không hợp lệ. Hãy kiểm tra tệp và tải lại."
                    : ex is ForbiddenException or UnauthorizedException
                        ? "Bạn không còn quyền đính kèm tệp vào hạng mục này."
                        : ex is ConflictException ? "Trạng thái hạng mục đã thay đổi. Hãy kiểm tra trước khi thử lưu lại."
                        : "Chưa thể lưu tệp. Dữ liệu đã tải vẫn được giữ; bạn có thể thử lưu lại.";
            await SaveAsync(entry with { State = "FAILED", Error = error }, CancellationToken.None);
        }
    }

    internal async Task CleanupAsync(CancellationToken ct, Func<Entry, CancellationToken, Task>? reconcile = null)
    {
        foreach (var entry in await EntriesAsync(ct))
        {
            if (entry.ExpiresAt > Now && entry.State != "CANCELLED") continue;
            await using var processing = await LockAsync(entry.SessionId, ".processing", ct);
            await using var gate = await LockAsync(entry.SessionId, ".lock", ct);
            try
            {
                var current = await ReadAsync(entry.SessionId, ct);
                if (current.ExpiresAt <= Now || current.State == "CANCELLED")
                {
                    if (current.State != "COMPLETED" && reconcile is not null) await reconcile(current, ct);
                    DeleteData(entry.SessionId);
                }
            }
            catch (NotFoundException) { }
        }
        // Recover files left before a create acknowledgement or after a completed metadata flush.
        foreach (var file in Directory.EnumerateFiles(root, "*.part"))
        {
            if (System.IO.File.GetLastWriteTimeUtc(file) > Now.Subtract(Lifetime)) continue;
            var id = Path.GetFileNameWithoutExtension(file);
            CheckId(id);
            await using var processing = await LockAsync(id, ".processing", ct);
            await using var gate = await LockAsync(id, ".lock", ct);
            try
            {
                var entry = await ReadAsync(id, ct);
                if (entry.State == "COMPLETED") System.IO.File.Delete(file);
            }
            catch (NotFoundException)
            {
                if (!System.IO.File.Exists(PathFor(id, ".quarantined"))) System.IO.File.Delete(file);
            }
        }
    }

    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["file"] = [message] });
}
