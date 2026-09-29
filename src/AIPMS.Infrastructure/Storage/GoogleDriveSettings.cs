using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Storage;

internal sealed class GoogleDriveSettings
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public string FolderId { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 60;
    public bool IsValid => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RefreshToken) && Regex.IsMatch(FolderId, "^[a-zA-Z0-9_-]{1,255}$")
        && TimeoutSeconds is >= 1 and <= 300;
    public static GoogleDriveSettings Read(IConfiguration config) => new()
    {
        ClientId = config["GoogleDrive:ClientId"] ?? "", ClientSecret = config["GoogleDrive:ClientSecret"] ?? "",
        RefreshToken = config["GoogleDrive:RefreshToken"] ?? "", FolderId = config["GoogleDrive:FolderId"] ?? "",
        TimeoutSeconds = int.TryParse(config["GoogleDrive:TimeoutSeconds"], out var timeout) ? timeout : 60
    };
}
