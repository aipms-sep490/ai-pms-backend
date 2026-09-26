using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Storage;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Storage;

internal sealed class GoogleDriveFileStorage : IFileStorage, IDisposable
{
    private readonly IGoogleDriveClient client;
    private readonly GoogleDriveSettings settings;
    // Bounded keyed locks prevent same-instance collisions without unbounded memory.
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public GoogleDriveFileStorage(IConfiguration configuration) : this(GoogleDriveSettings.Read(configuration), null) { }
    internal GoogleDriveFileStorage(GoogleDriveSettings settings, IGoogleDriveClient? client)
    {
        if (!settings.IsValid) throw new InvalidOperationException("GoogleDrive requires credentials, an explicit FolderId and a timeout of 1-300 seconds.");
        this.settings = settings;
        this.client = client ?? new GoogleDriveClient(settings);
    }
    public async Task WriteAsync(string key, Stream content, CancellationToken ct)
    {
        ValidateKey(key);
        await ExecuteAsync(async token =>
        {
            var gate = gates[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)gates.Length];
            await gate.WaitAsync(token);
            try
            {
                if (await FindIdAsync(key, token) is not null) throw new IOException("Google Drive object already exists.");
                await client.UploadAsync(settings.FolderId, key, content, token);
            }
            finally { gate.Release(); }
        }, ct);
    }
    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        var output = new MemoryStream();
        try
        {
            await ExecuteAsync(async token =>
            {
                var id = await FindIdAsync(key, token) ?? throw new FileNotFoundException("Google Drive object was not found.");
                await client.DownloadAsync(id, output, token);
            }, ct);
            output.Position = 0;
            return output;
        }
        catch { output.Dispose(); throw; }
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
    private async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
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
}
