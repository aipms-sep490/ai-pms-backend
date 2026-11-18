using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Files;

namespace AIPMS.Application.Features.Deliverables.Services;

/// <summary>Validates a staged project file without materializing its contents in memory.</summary>
public static class ProjectFileUploadValidator
{
    public const long MaxArchiveExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxOfficeExpandedBytes = 200L * 1024 * 1024;
    private const uint MaxCentralDirectoryBytes = 16 * 1024 * 1024;

    public static void ValidateMetadata(string fileName, string contentType, long length)
    {
        if (length > UploadPolicies.ProjectFileLimit)
            throw new PayloadTooLargeException(UploadPolicies.ProjectFileLimit, "Tệp vượt giới hạn 500 MiB.");
        if (string.IsNullOrWhiteSpace(fileName) || fileName != fileName.Trim() || fileName.Length > 255
            || fileName.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) || fileName.EndsWith('.') || length <= 0)
            throw Invalid("Tên hoặc kích thước tệp không hợp lệ.");
        var mime = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".zip" => "application/zip",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => null
        };
        if (mime is null || !string.Equals(mime, contentType, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Chỉ hỗ trợ PDF, DOCX, ZIP, PNG, JPG, JPEG với định dạng nội dung tương ứng.");
    }

    public static async Task<UploadMetadata> ValidateAsync(Stream content, string fileName, string contentType,
        long length, CancellationToken ct)
    {
        ValidateMetadata(fileName, contentType, length);
        if (!content.CanSeek || content.Length != length) throw Invalid("Kích thước tệp không khớp nội dung đã tải.");
        content.Position = 0;
        var header = new byte[8];
        var count = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var valid = extension switch
        {
            ".pdf" => header.AsSpan(0, count).StartsWith("%PDF-"u8),
            ".png" => header.AsSpan(0, count).StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => header.AsSpan(0, count).StartsWith(new byte[] { 255, 216, 255 }),
            _ => await ValidateArchiveAsync(content, extension, ct)
        };
        if (!valid) throw Invalid("Nội dung tệp không đúng định dạng hoặc vượt giới hạn an toàn của tệp nén.");
        content.Position = 0;
        var checksum = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
        content.Position = 0;
        return new(fileName, contentType, length, checksum);
    }

    private static async Task<bool> ValidateArchiveAsync(Stream content, string extension, CancellationToken ct)
    {
        // Bound central-directory allocation before ZipArchive parses any entry objects. ZIP64 is
        // unnecessary for a <=500 MiB project archive with <=10,000 entries and is rejected.
        var tail = new byte[(int)Math.Min(content.Length, 65557)];
        content.Position = content.Length - tail.Length;
        await content.ReadExactlyAsync(tail, ct);
        var end = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != 0x06054b50) continue;
            if (i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) == tail.Length)
            { end = i; break; }
        }
        if (end < 0) return false;
        var record = tail.AsMemory(end, 22);
        if (BinaryPrimitives.ReadUInt16LittleEndian(record.Span[4..]) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(record.Span[6..]) != 0) return false;
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(record.Span[10..]);
        var directoryBytes = BinaryPrimitives.ReadUInt32LittleEndian(record.Span[12..]);
        var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record.Span[16..]);
        if (entries is 0 or > 10000 || BinaryPrimitives.ReadUInt16LittleEndian(record.Span[8..]) != entries
            || directoryBytes > MaxCentralDirectoryBytes
            || (long)directoryOffset + directoryBytes > content.Length - tail.Length + end) return false;
        // Do not trust the EOCD's claimed size alone: some ZIP readers continue parsing beyond it.
        // Bound every actual directory header/name/extra/comment before allocating entry objects.
        content.Position = directoryOffset;
        var directoryHeader = new byte[46];
        long consumed = 0;
        for (var index = 0; index < entries; index++)
        {
            if (directoryBytes - consumed < directoryHeader.Length) return false;
            await content.ReadExactlyAsync(directoryHeader, ct);
            if (BinaryPrimitives.ReadUInt32LittleEndian(directoryHeader) != 0x02014b50
                || BinaryPrimitives.ReadUInt16LittleEndian(directoryHeader.AsSpan(34)) != 0) return false;
            var variableBytes = BinaryPrimitives.ReadUInt16LittleEndian(directoryHeader.AsSpan(28))
                + BinaryPrimitives.ReadUInt16LittleEndian(directoryHeader.AsSpan(30))
                + BinaryPrimitives.ReadUInt16LittleEndian(directoryHeader.AsSpan(32));
            consumed += directoryHeader.Length + variableBytes;
            if (consumed > directoryBytes) return false;
            content.Seek(variableBytes, SeekOrigin.Current);
        }
        if (consumed != directoryBytes) return false;
        try
        {
            content.Position = 0;
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count != entries) return false;
            long expandedBytes = 0;
            var ceiling = extension == ".docx" ? MaxOfficeExpandedBytes : MaxArchiveExpandedBytes;
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Length > ceiling - expandedBytes) return false;
                expandedBytes += entry.Length;
            }
            return extension != ".docx" || (archive.GetEntry("[Content_Types].xml") is not null
                && archive.GetEntry("word/document.xml") is not null
                && !archive.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)));
        }
        catch (InvalidDataException) { return false; }
    }

    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["file"] = [message] });
}
