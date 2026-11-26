using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Files.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPMS.Infrastructure.Services.Files;

internal sealed record GotenbergOfficePdfSettings(
    string BaseUrl,
    int TimeoutSeconds,
    int MaxFileBytes,
    string CacheDirectory,
    long MaxCacheBytes,
    int MaxConcurrentConversions)
{
    public bool IsEnabled => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static GotenbergOfficePdfSettings Read(IConfiguration config)
    {
        var baseUrl = (config["OfficePreview:GotenbergUrl"] ?? "").Trim().TrimEnd('/');
        var timeout = int.TryParse(config["OfficePreview:TimeoutSeconds"], out var t) ? Math.Clamp(t, 5, 120) : 45;
        var maxBytes = int.TryParse(config["OfficePreview:MaxFileBytes"], out var m) ? Math.Clamp(m, 1024 * 1024, 50 * 1024 * 1024) : 20 * 1024 * 1024;
        var maxCache = long.TryParse(config["OfficePreview:MaxCacheBytes"], out var c) ? Math.Clamp(c, 16L * 1024 * 1024, 2L * 1024 * 1024 * 1024) : 256L * 1024 * 1024;
        var concurrency = int.TryParse(config["OfficePreview:MaxConcurrentConversions"], out var cc) ? Math.Clamp(cc, 1, 4) : 2;
        var root = config["OfficePreview:CacheDirectory"];
        if (string.IsNullOrWhiteSpace(root))
        {
            var storageRoot = config["FileStorage:RootPath"];
            root = !string.IsNullOrWhiteSpace(storageRoot)
                ? Path.Combine(storageRoot, ".office-pdf-cache")
                : Path.Combine(Path.GetTempPath(), "aipms-office-pdf-cache");
        }
        return new(baseUrl, timeout, maxBytes, Path.GetFullPath(root), maxCache, concurrency);
    }
}

internal sealed class GotenbergOfficePdfConverter : IOfficePdfConverter, IDisposable
{
    private static readonly HashSet<string> ZipOfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp"
    };

    private static readonly HashSet<string> OleOfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".xls", ".ppt"
    };

    private static readonly byte[] OleMagic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46, 0x2D]; // %PDF-
    private const long MaxZipExpandedBytes = 100L * 1024 * 1024;

    private readonly GotenbergOfficePdfSettings settings;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly ILogger<GotenbergOfficePdfConverter> logger;
    private readonly SemaphoreSlim conversionSlots;
    private readonly object pruneLock = new();

    public GotenbergOfficePdfConverter(IConfiguration configuration, ILogger<GotenbergOfficePdfConverter> logger)
        : this(GotenbergOfficePdfSettings.Read(configuration), CreateDefaultClient(), true, logger)
    {
    }

    internal GotenbergOfficePdfConverter(
        GotenbergOfficePdfSettings settings,
        HttpClient httpClient,
        ILogger<GotenbergOfficePdfConverter> logger)
        : this(settings, httpClient, false, logger)
    {
    }

    private GotenbergOfficePdfConverter(
        GotenbergOfficePdfSettings settings,
        HttpClient httpClient,
        bool ownsHttpClient,
        ILogger<GotenbergOfficePdfConverter> logger)
    {
        this.settings = settings;
        this.httpClient = httpClient;
        this.ownsHttpClient = ownsHttpClient;
        this.logger = logger;
        conversionSlots = new SemaphoreSlim(settings.MaxConcurrentConversions, settings.MaxConcurrentConversions);
    }

    public bool IsEnabled => settings.IsEnabled;

    public async Task<byte[]> ConvertToPdfAsync(string fileName, byte[] bytes, CancellationToken ct = default)
    {
        var ext = ValidateInput(fileName, bytes);
        if (!IsEnabled)
            throw new ServiceUnavailableException("Office PDF preview service is not configured.", "OFFICE_PDF_PREVIEW_DISABLED");

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var cachePath = Path.Combine(settings.CacheDirectory, $"{sha256}.pdf");
        var cached = await TryReadCacheAsync(cachePath, ct);
        if (cached is not null) return cached;

        await conversionSlots.WaitAsync(ct);
        try
        {
            cached = await TryReadCacheAsync(cachePath, ct);
            if (cached is not null) return cached;

            var pdfBytes = await ConvertViaGotenbergAsync(ext, bytes, ct);
            await TryWriteCacheAsync(cachePath, pdfBytes, ct);
            return pdfBytes;
        }
        finally
        {
            conversionSlots.Release();
        }
    }

    private string ValidateInput(string fileName, byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length > settings.MaxFileBytes)
            throw Invalid($"Office PDF preview accepts files up to {settings.MaxFileBytes / (1024 * 1024)} MiB.");

        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (ZipOfficeExtensions.Contains(ext))
        {
            if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
                throw Invalid("The Office file header is invalid.");
            try
            {
                using var archive = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
                if (archive.Entries.Count is < 2 or > 2500)
                    throw Invalid("The Office package structure is invalid.");
                long expanded = 0;
                foreach (var entry in archive.Entries)
                {
                    expanded += entry.Length;
                    if (expanded > MaxZipExpandedBytes
                        || entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase))
                        throw Invalid("The Office package contains unsafe or excessive content.");
                }
            }
            catch (InvalidDataException)
            {
                throw Invalid("The Office package is damaged.");
            }
            return ext;
        }

        if (OleOfficeExtensions.Contains(ext))
        {
            if (bytes.Length < OleMagic.Length || !bytes.AsSpan(0, OleMagic.Length).SequenceEqual(OleMagic))
                throw Invalid("The legacy Office file header is invalid.");
            return ext;
        }

        throw Invalid("Only Word, Excel, PowerPoint, and OpenDocument files can be rendered to PDF.");
    }

    private async Task<byte[]> ConvertViaGotenbergAsync(string ext, byte[] bytes, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        // Always pass a safe ASCII filename to avoid Unicode filename quirks in multipart/LibreOffice.
        form.Add(fileContent, "files", $"document{ext}");

        var endpoint = $"{settings.BaseUrl}/forms/libreoffice/convert";
        try
        {
            using var response = await httpClient.PostAsync(endpoint, form, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gotenberg LibreOffice conversion returned HTTP {StatusCode} for {Extension} ({Length} bytes).",
                    (int)response.StatusCode, ext, bytes.Length);
                throw new ServiceUnavailableException("Office PDF preview service could not convert this document.", "OFFICE_PDF_PREVIEW_UNAVAILABLE");
            }

            var pdf = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
            if (pdf.Length < PdfMagic.Length || !pdf.AsSpan(0, PdfMagic.Length).SequenceEqual(PdfMagic))
            {
                logger.LogWarning("Gotenberg returned a non-PDF payload ({Length} bytes) for {Extension}.", pdf.Length, ext);
                throw new ServiceUnavailableException("Office PDF preview service returned an invalid PDF.", "OFFICE_PDF_PREVIEW_UNAVAILABLE");
            }
            return pdf;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Gotenberg LibreOffice conversion timed out after {TimeoutSeconds}s for {Extension}.", settings.TimeoutSeconds, ext);
            throw new ServiceUnavailableException("Office PDF preview conversion timed out.", "OFFICE_PDF_PREVIEW_TIMEOUT");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Gotenberg LibreOffice conversion endpoint is unreachable.");
            throw new ServiceUnavailableException("Office PDF preview service is temporarily unavailable.", "OFFICE_PDF_PREVIEW_UNAVAILABLE");
        }
    }

    private static async Task<byte[]?> TryReadCacheAsync(string cachePath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(cachePath)) return null;
            var cached = await File.ReadAllBytesAsync(cachePath, ct);
            if (cached.Length >= PdfMagic.Length && cached.AsSpan(0, PdfMagic.Length).SequenceEqual(PdfMagic))
            {
                try { File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow); } catch { /* best effort */ }
                return cached;
            }
        }
        catch
        {
            // Cache miss on read failure; proceed with fresh conversion.
        }
        return null;
    }

    private async Task TryWriteCacheAsync(string cachePath, byte[] pdfBytes, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(settings.CacheDirectory);
            var tmpPath = Path.Combine(settings.CacheDirectory, $"{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(tmpPath, pdfBytes, ct);
            File.Move(tmpPath, cachePath, overwrite: true);
            PruneCacheBestEffort();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Skipping Office PDF preview cache write for {CachePath}.", cachePath);
        }
    }

    private void PruneCacheBestEffort()
    {
        lock (pruneLock)
        {
            try
            {
                var dir = new DirectoryInfo(settings.CacheDirectory);
                if (!dir.Exists) return;
                var files = dir.GetFiles("*.pdf");
                long total = files.Sum(f => f.Length);
                if (total <= settings.MaxCacheBytes) return;
                var target = (long)(settings.MaxCacheBytes * 0.8);
                foreach (var file in files.OrderBy(f => f.LastAccessTimeUtc))
                {
                    if (total <= target) break;
                    try
                    {
                        var len = file.Length;
                        file.Delete();
                        total -= len;
                    }
                    catch { /* in-use file */ }
                }
            }
            catch { /* best effort */ }
        }
    }

    private static ValidationException Invalid(string reason) =>
        new(new Dictionary<string, string[]> { ["file"] = [reason] });

    private static HttpClient CreateDefaultClient() =>
        new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    public void Dispose()
    {
        if (ownsHttpClient) httpClient.Dispose();
        conversionSlots.Dispose();
    }
}
