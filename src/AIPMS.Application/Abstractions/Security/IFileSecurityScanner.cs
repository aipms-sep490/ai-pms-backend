namespace AIPMS.Application.Abstractions.Security;

public static class FileScanStatuses
{
    public const string Uploading = "UPLOADING";
    public const string Scanning = "SCANNING";
    public const string Ready = "READY";
    public const string Quarantined = "QUARANTINED";
    public const string PreviewFailed = "PREVIEW_FAILED";
}

public sealed record FileScanResult(
    string Status,
    string Provider,
    string? Details,
    bool IsSafe)
{
    public static FileScanResult Safe(string provider, string? details = null) =>
        new(FileScanStatuses.Ready, provider, details, true);

    public static FileScanResult Quarantined(string provider, string details) =>
        new(FileScanStatuses.Quarantined, provider, details, false);
}

public interface IFileSecurityScanner
{
    string ProviderName { get; }
    Task<FileScanResult> ScanAsync(Stream content, string fileName, string contentType, CancellationToken ct);
}
