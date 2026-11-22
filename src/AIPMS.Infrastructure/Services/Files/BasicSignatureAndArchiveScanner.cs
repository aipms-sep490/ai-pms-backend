using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;

namespace AIPMS.Infrastructure.Services.Files;

/// <summary>
/// Adapter implementation of IFileSecurityScanner for local/dev/staging environments.
/// Validates magic bytes, rejects executable disguises (MZ/ELF/Mach-O/shebang), Office macros
/// (vbaProject.bin / external links), path traversal entries, and zip-bombs.
/// </summary>
public sealed class BasicSignatureAndArchiveScanner : IFileSecurityScanner
{
    private const long MaxOfficeExpandedBytes = 200L * 1024 * 1024;
    private const long MaxZipExpandedBytes = 500L * 1024 * 1024;
    private const int MaxZipEntries = 10_000;
    private const double MaxCompressionRatio = 100.0;

    public string ProviderName => "BasicSignatureAndArchiveScanner";

    public async Task<FileScanResult> ScanAsync(Stream content, string fileName, string contentType, CancellationToken ct)
    {
        if (content is null || !content.CanRead)
            return FileScanResult.Quarantined(ProviderName, "Unreadable file stream.");

        var originalPos = content.CanSeek ? content.Position : 0;
        if (content.CanSeek) content.Position = 0;

        try
        {
            var header = new byte[16];
            var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
            if (read == 0)
                return FileScanResult.Quarantined(ProviderName, "Empty file content.");

            // Reject disguised executable payloads immediately.
            if (IsExecutableSignature(header.AsSpan(0, read)))
                return FileScanResult.Quarantined(ProviderName, "Executable binary signature detected inside uploaded file.");

            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            if (ext is ".exe" or ".dll" or ".bat" or ".cmd" or ".ps1" or ".sh" or ".js" or ".vbs" or ".docm" or ".xlsm" or ".pptm")
                return FileScanResult.Quarantined(ProviderName, $"Disallowed executable or macro-enabled extension '{ext}'.");

            if (ext == ".pdf")
                return header.AsSpan(0, read).StartsWith("%PDF-"u8)
                    ? FileScanResult.Safe(ProviderName)
                    : FileScanResult.Quarantined(ProviderName, "Declared PDF does not start with %PDF- magic bytes.");

            if (ext == ".png")
                return header.AsSpan(0, read).StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
                    ? FileScanResult.Safe(ProviderName)
                    : FileScanResult.Quarantined(ProviderName, "Declared PNG does not match PNG magic signature.");

            if (ext is ".jpg" or ".jpeg")
                return header.AsSpan(0, read).StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })
                    ? FileScanResult.Safe(ProviderName)
                    : FileScanResult.Quarantined(ProviderName, "Declared JPEG does not match JPEG magic signature.");

            if (ext == ".mp4")
            {
                var hasFtyp = read >= 8 && header.AsSpan(4, 4).SequenceEqual("ftyp"u8);
                return hasFtyp
                    ? FileScanResult.Safe(ProviderName)
                    : FileScanResult.Quarantined(ProviderName, "Declared MP4 video does not contain ISO BMFF ftyp box.");
            }

            if (ext == ".txt")
            {
                if (content.CanSeek) content.Position = 0;
                using var reader = new StreamReader(content, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var text = await reader.ReadToEndAsync(ct);
                return text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
                    ? FileScanResult.Quarantined(ProviderName, "Plain text file contains binary control bytes.")
                    : FileScanResult.Safe(ProviderName);
            }

            if (ext is ".zip" or ".docx" or ".xlsx" or ".pptx")
            {
                if (read < 4 || header[0] != 0x50 || header[1] != 0x4B)
                    return FileScanResult.Quarantined(ProviderName, $"Declared '{ext}' archive does not start with PK ZIP header.");
                if (content.CanSeek) content.Position = 0;
                return ScanArchive(content, ext, ct);
            }

            return FileScanResult.Quarantined(ProviderName, $"Unsupported file extension '{ext}'.");
        }
        catch (DecoderFallbackException)
        {
            return FileScanResult.Quarantined(ProviderName, "Invalid UTF-8 encoding in text upload.");
        }
        catch (InvalidDataException)
        {
            return FileScanResult.Quarantined(ProviderName, "Corrupted archive structure.");
        }
        finally
        {
            if (content.CanSeek) content.Position = originalPos;
        }
    }

    private FileScanResult ScanArchive(Stream content, string ext, CancellationToken ct)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count is 0 or > MaxZipEntries)
            return FileScanResult.Quarantined(ProviderName, "Archive entry count is empty or exceeds safety limit.");

        var isOffice = ext is ".docx" or ".xlsx" or ".pptx";
        var maxExpanded = isOffice ? MaxOfficeExpandedBytes : MaxZipExpandedBytes;
        long totalExpanded = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var entryName = entry.FullName.Replace('\\', '/');
            if (entryName.Contains("../", StringComparison.Ordinal) || entryName.StartsWith('/') || entryName.Contains(':'))
                return FileScanResult.Quarantined(ProviderName, $"Archive contains unsafe path traversal entry '{entry.FullName}'.");

            if (entryName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)
                || entryName.Contains("vbaData.xml", StringComparison.OrdinalIgnoreCase))
                return FileScanResult.Quarantined(ProviderName, "Office macro payload (vbaProject.bin) is forbidden.");

            if (isOffice && entryName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase))
                return FileScanResult.Quarantined(ProviderName, "External workbook links are forbidden.");

            if (entry.Length > maxExpanded - totalExpanded)
                return FileScanResult.Quarantined(ProviderName, "Archive expanded size exceeds zip-bomb safety threshold.");

            totalExpanded += entry.Length;

            if (entry.Length > 100 * 1024 && entry.CompressedLength > 0)
            {
                var ratio = (double)entry.Length / entry.CompressedLength;
                if (ratio > MaxCompressionRatio)
                    return FileScanResult.Quarantined(ProviderName, $"Potential zip-bomb detected (compression ratio {ratio:F1}:1).");
            }
        }

        if (isOffice)
        {
            var requiredPart = ext switch
            {
                ".docx" => "word/document.xml",
                ".xlsx" => "xl/workbook.xml",
                ".pptx" => "ppt/presentation.xml",
                _ => null
            };
            if (archive.GetEntry("[Content_Types].xml") is null
                || (requiredPart is not null && archive.GetEntry(requiredPart) is null))
                return FileScanResult.Quarantined(ProviderName, $"Office package '{ext}' is missing required OpenXML structure.");
        }

        return FileScanResult.Safe(ProviderName);
    }

    private static bool IsExecutableSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 2 && header[0] == 0x4D && header[1] == 0x5A) return true; // PE / MZ (.exe/.dll)
        if (header.Length >= 4 && header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F') return true; // ELF
        if (header.Length >= 4 && ((header[0] == 0xCF && header[1] == 0xFA && header[2] == 0xED && header[3] == 0xFE)
            || (header[0] == 0xFE && header[1] == 0xED && header[2] == 0xFA && header[3] == 0xCF))) return true; // Mach-O
        if (header.Length >= 2 && header[0] == (byte)'#' && header[1] == (byte)'!') return true; // Shebang script
        return false;
    }
}
