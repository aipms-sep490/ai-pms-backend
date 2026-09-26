using System.IO;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace AIPMS.Infrastructure.Storage;

internal sealed class GoogleDriveClient : IGoogleDriveClient, IDisposable
{
    private readonly DriveService drive;
    private readonly GoogleAuthorizationCodeFlow flow;
    public GoogleDriveClient(GoogleDriveSettings settings)
    {
        flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = settings.ClientId, ClientSecret = settings.ClientSecret },
            Scopes = [DriveService.Scope.DriveFile], DataStore = new NullDataStore()
        });
        var credential = new UserCredential(flow, "aipms-drive", new Google.Apis.Auth.OAuth2.Responses.TokenResponse { RefreshToken = settings.RefreshToken });
        drive = new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "AI-PMS" });
        drive.HttpClient.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    }
    public async Task<IReadOnlyList<string>> FindAsync(string folderId, string key, CancellationToken ct)
    {
        var request = drive.Files.List();
        request.Q = $"name = '{key}' and trashed = false and '{folderId}' in parents";
        request.Fields = "files(id)"; request.PageSize = 2;
        var result = await request.ExecuteAsync(ct);
        return result.Files.Select(x => x.Id).ToArray();
    }
    public async Task UploadAsync(string folderId, string key, Stream content, CancellationToken ct)
    {
        // No application retry: an uncertain response must not create another object.
        var metadata = new Google.Apis.Drive.v3.Data.File { Name = key, Parents = [folderId] };
        var request = drive.Files.Create(metadata, content, "application/octet-stream");
        request.Fields = "id";
        var result = await request.UploadAsync(ct);
        if (result.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw result.Exception ?? new IOException("Drive upload did not complete.");
    }
    public async Task DownloadAsync(string id, Stream output, CancellationToken ct)
    {
        var result = await drive.Files.Get(id).DownloadAsync(output, ct);
        if (result.Status != Google.Apis.Download.DownloadStatus.Completed)
            throw result.Exception ?? new IOException("Drive download did not complete.");
    }
    public async Task DeleteAsync(string id, CancellationToken ct) => await drive.Files.Delete(id).ExecuteAsync(ct);
    public async Task CheckFolderAsync(string folderId, CancellationToken ct)
    {
        var request = drive.Files.Get(folderId);
        request.Fields = "id,mimeType,trashed,capabilities(canAddChildren)";
        var result = await request.ExecuteAsync(ct);
        if (result.MimeType != "application/vnd.google-apps.folder" || result.Trashed == true || result.Capabilities?.CanAddChildren != true)
            throw new IOException("Configured Drive folder is not writable.");
    }
    public void Dispose() { drive.Dispose(); flow.Dispose(); }
    private sealed class NullDataStore : IDataStore
    {
        public Task ClearAsync() => Task.CompletedTask;
        public Task DeleteAsync<T>(string key) => Task.CompletedTask;
        public Task<T> GetAsync<T>(string key) => Task.FromResult<T>(default!);
        public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;
    }
}
