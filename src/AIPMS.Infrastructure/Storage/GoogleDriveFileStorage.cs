using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using AIPMS.Application.Abstractions.Storage;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Storage;

internal sealed class GoogleDriveFileStorage : IFileStorage
{
    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];
    private readonly DriveService drive;
    private string? folderId;

    public GoogleDriveFileStorage(IConfiguration configuration)
    {
        var clientId = Required(configuration, "GoogleDrive:ClientId");
        var clientSecret = Required(configuration, "GoogleDrive:ClientSecret");
        var refreshToken = Required(configuration, "GoogleDrive:RefreshToken");
        folderId = configuration["GoogleDrive:FolderId"];

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = Scopes,
            DataStore = new NullDataStore()
        });
        var credential = new UserCredential(flow, "aipms-drive", new Google.Apis.Auth.OAuth2.Responses.TokenResponse
        {
            RefreshToken = refreshToken
        });
        drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AI-PMS"
        });
    }

    public async Task WriteAsync(string key, Stream content, CancellationToken ct)
    {
        ValidateKey(key);
        if (await FindIdAsync(key, ct) is not null) throw new IOException("Google Drive object already exists.");
        var metadata = new Google.Apis.Drive.v3.Data.File { Name = key, Parents = [await EnsureFolderAsync(ct)] };
        var request = drive.Files.Create(metadata, content, "application/octet-stream");
        request.Fields = "id,name";
        var result = await request.UploadAsync(ct);
        if (result.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw result.Exception ?? new IOException("Google Drive upload failed.");
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        var id = await FindIdAsync(key, ct) ?? throw new FileNotFoundException("Google Drive object was not found.");
        var output = new MemoryStream();
        var response = await drive.Files.Get(id).DownloadAsync(output, ct);
        if (response.Status != Google.Apis.Download.DownloadStatus.Completed)
        {
            output.Dispose();
            throw response.Exception ?? new IOException("Google Drive download failed.");
        }
        output.Position = 0;
        return output;
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        var id = await FindIdAsync(key, ct);
        if (id is not null) await drive.Files.Delete(id).ExecuteAsync(ct);
    }

    private async Task<string?> FindIdAsync(string key, CancellationToken ct)
    {
        var request = drive.Files.List();
        request.Q = $"name = '{key}' and trashed = false" +
            (string.IsNullOrWhiteSpace(folderId) ? string.Empty : $" and '{folderId}' in parents");
        request.Fields = "files(id)";
        request.PageSize = 2;
        var files = await request.ExecuteAsync(ct);
        return files.Files?.SingleOrDefault()?.Id;
    }

    private async Task<string> EnsureFolderAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(folderId)) return folderId;
        var request = drive.Files.List();
        request.Q = "name = 'AI-PMS' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        request.Fields = "files(id)";
        request.PageSize = 2;
        var existing = (await request.ExecuteAsync(ct)).Files?.SingleOrDefault()?.Id;
        if (existing is not null) return folderId = existing;
        var created = await drive.Files.Create(new Google.Apis.Drive.v3.Data.File
        {
            Name = "AI-PMS",
            MimeType = "application/vnd.google-apps.folder"
        }).ExecuteAsync(ct);
        return folderId = created.Id ?? throw new IOException("Google Drive folder creation failed.");
    }

    private static string Required(IConfiguration c, string key) =>
        !string.IsNullOrWhiteSpace(c[key]) ? c[key]! : throw new InvalidOperationException($"Missing configuration: {key}");

    private static void ValidateKey(string key)
    {
        if (!Regex.IsMatch(key, "^[a-fA-F0-9]{32}$")) throw new ArgumentException("Invalid storage key.", nameof(key));
    }

    private sealed class NullDataStore : IDataStore
    {
        public Task ClearAsync() => Task.CompletedTask;
        public Task DeleteAsync<T>(string key) => Task.CompletedTask;
        public Task<T> GetAsync<T>(string key) => Task.FromResult<T>(default!);
        public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;
    }
}
