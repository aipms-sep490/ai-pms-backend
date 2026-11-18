using System.Text.RegularExpressions;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Storage;

internal sealed class GoogleDriveSettings
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public string FolderId { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 60;
    public int TransferTimeoutSeconds { get; init; } = 1800;
    public string DownloadSpoolPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIPMS", "private-files", ".downloads");
    public int MaxConcurrentDownloads { get; init; } = 4;
    public bool IsValid => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RefreshToken) && Regex.IsMatch(FolderId, "^[a-zA-Z0-9_-]{1,255}$")
        && TimeoutSeconds is >= 1 and <= 300
        && TransferTimeoutSeconds is >= 1 and <= 3600
        && MaxConcurrentDownloads is >= 1 and <= 16
        && !string.IsNullOrWhiteSpace(DownloadSpoolPath);
    public static GoogleDriveSettings Read(IConfiguration config) => new()
    {
        ClientId = config["GoogleDrive:ClientId"] ?? "", ClientSecret = config["GoogleDrive:ClientSecret"] ?? "",
        RefreshToken = config["GoogleDrive:RefreshToken"] ?? "", FolderId = config["GoogleDrive:FolderId"] ?? "",
        TimeoutSeconds = int.TryParse(config["GoogleDrive:TimeoutSeconds"], out var timeout) ? timeout : 60,
        TransferTimeoutSeconds = int.TryParse(config["GoogleDrive:TransferTimeoutSeconds"], out var transferTimeout) ? transferTimeout : 1800,
        DownloadSpoolPath = config["GoogleDrive:DownloadSpoolPath"] ?? Path.Combine(
            string.IsNullOrWhiteSpace(config["FileStorage:RootPath"])
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIPMS", "private-files")
                : config["FileStorage:RootPath"]!, ".downloads"),
        MaxConcurrentDownloads = int.TryParse(config["GoogleDrive:MaxConcurrentDownloads"], out var downloads) ? downloads : 4
    };
}
