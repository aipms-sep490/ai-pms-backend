using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Storage;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Storage;

internal sealed partial class GoogleDriveFileStorage : IStreamingFileStorage, IDisposable
{
    private readonly IGoogleDriveClient client;
    private readonly GoogleDriveSettings settings;
    private readonly string downloadRoot;
    private readonly SemaphoreSlim downloadSlots;
    // Bounded keyed locks prevent same-instance collisions without unbounded memory.
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public GoogleDriveFileStorage(IConfiguration configuration) : this(GoogleDriveSettings.Read(configuration), null) { }
    internal GoogleDriveFileStorage(GoogleDriveSettings settings, IGoogleDriveClient? client)
    {
        if (!settings.IsValid) throw new InvalidOperationException("GoogleDrive requires credentials, an explicit FolderId, a metadata timeout of 1-300 seconds and a transfer timeout of 1-3600 seconds.");
        this.settings = settings;
        this.client = client ?? new GoogleDriveClient(settings);
        downloadRoot = Path.GetFullPath(settings.DownloadSpoolPath);
        Directory.CreateDirectory(downloadRoot);
        // Only inactive provider-owned spool files can be removed after a process restart.
        foreach (var path in Directory.EnumerateFiles(downloadRoot, "aipms-drive-*.tmp"))
        {
            var stem = Path.GetFileNameWithoutExtension(path)["aipms-drive-".Length..];
            if (!Guid.TryParseExact(stem, "N", out _)) continue;
            try
            {
                using var stale = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.DeleteOnClose);
            }
            catch (IOException) { /* An active download still owns the file. */ }
        }
        downloadSlots = new SemaphoreSlim(settings.MaxConcurrentDownloads, settings.MaxConcurrentDownloads);
    }
    public async Task WriteAsync(string key, Stream content, CancellationToken ct)
    {
        ValidateKey(key);
        var gate = gates[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)gates.Length];
        await ExecuteAsync(token => gate.WaitAsync(token), ct);
        try
        {
            await ExecuteAsync(async token =>
            {
                if (await FindIdAsync(key, token) is not null) throw new IOException("Google Drive object already exists.");
            }, ct);
            await ExecuteAsync(token => client.UploadAsync(settings.FolderId, key, content, token), ct, settings.TransferTimeoutSeconds);
        }
        finally { gate.Release(); }
    }
    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        string? id = null;
        await ExecuteAsync(async token => id = await FindIdAsync(key, token)
            ?? throw new FileNotFoundException("Google Drive object was not found."), ct);
        await ExecuteAsync(token => downloadSlots.WaitAsync(token), ct);
        DownloadSpoolStream output;
        try { output = new DownloadSpoolStream(Path.Combine(downloadRoot, $"aipms-drive-{Guid.NewGuid():N}.tmp"), downloadSlots); }
        catch { downloadSlots.Release(); throw; }
        try
        {
            await ExecuteAsync(token => client.DownloadAsync(id!, output, token), ct, settings.TransferTimeoutSeconds);
            await output.FlushAsync(ct);
            output.Position = 0;
            return output;
        }
        catch { await output.DisposeAsync(); throw; }
    }
    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        await ExecuteAsync(async token =>
        {
            var id = await FindIdAsync(key, token);
            if (id is not null) await client.DeleteAsync(id, token);
        }, ct);
    }
    internal Task CheckFolderAsync(CancellationToken ct) => ExecuteAsync(token => client.CheckFolderAsync(settings.FolderId, token), ct);
    private async Task<string?> FindIdAsync(string key, CancellationToken ct)
    {
        var ids = await client.FindAsync(settings.FolderId, key, ct);
        if (ids.Count > 1) throw new IOException("Multiple Drive objects have the same storage key; manual reconciliation is required.");
        return ids.SingleOrDefault();
    }
    private async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct, int? timeoutSeconds = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds ?? settings.TimeoutSeconds));
        try { await operation(timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("Google Drive operation timed out; its remote result may be unknown."); }
        catch (Exception ex) when (ex is Google.GoogleApiException or Google.Apis.Auth.OAuth2.Responses.TokenResponseException or HttpRequestException)
        { throw new IOException("Google Drive is unavailable or access was denied."); }
    }
    private static void ValidateKey(string key)
    {
        if (key.Length != 32 || key.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid storage key.", nameof(key));
    }
    public void Dispose()
    {
        if (client is IDisposable disposable) disposable.Dispose();
        foreach (var gate in gates) gate.Dispose();
    }

    // Four default slots reserve at most 2 GiB, including streams still being sent to clients.
    private sealed class DownloadSpoolStream(string path, SemaphoreSlim slots) : FileStream(path,
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
        FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose)
    {
        private const long MaxBytes = 500L * 1024 * 1024;
        private int released;
        private void CheckWrite(int count)
        {
            if (Position > MaxBytes - count) throw new IOException("Google Drive file exceeds the 500 MiB download limit.");
        }
        public override void Write(byte[] buffer, int offset, int count) { CheckWrite(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { CheckWrite(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { CheckWrite(1); base.WriteByte(value); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        { CheckWrite(count); return base.WriteAsync(buffer, offset, count, ct); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { CheckWrite(buffer.Length); return base.WriteAsync(buffer, ct); }
        public override void SetLength(long value)
        {
            if (value > MaxBytes) throw new IOException("Google Drive file exceeds the 500 MiB download limit.");
            base.SetLength(value);
        }
        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally { if (disposing && Interlocked.Exchange(ref released, 1) == 0) slots.Release(); }
        }
        public override async ValueTask DisposeAsync()
        {
            try { await base.DisposeAsync(); }
            finally { if (Interlocked.Exchange(ref released, 1) == 0) slots.Release(); }
        }
    }
}
