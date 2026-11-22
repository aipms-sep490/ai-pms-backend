using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.Models;

namespace AIPMS.Application.Features.Deliverables.Services;

public static class UploadValidator
{
    public const int MaxBytes = 20 * 1024 * 1024;
    public const long MaxArchiveVideoBytes = 100L * 1024 * 1024;
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".png"] = "image/png",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".zip"] = "application/zip",
        [".mp4"] = "video/mp4",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

    public static bool IsArchiveOrVideo(string extension) =>
        extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);

    public static long EffectiveLimitFor(string fileName, bool allowArchiveVideoTier)
    {
        var ext = Path.GetExtension(fileName?.Trim() ?? string.Empty);
        return allowArchiveVideoTier && IsArchiveOrVideo(ext) ? MaxArchiveVideoBytes : MaxBytes;
    }

    public static Task<ValidatedUpload> ReadAsync(UploadContent upload, CancellationToken ct) =>
        ReadAsync(upload, ct, allowArchiveVideoTier: false, allowQuarantineFallback: false);

    public static async Task<ValidatedUpload> ReadAsync(UploadContent upload, CancellationToken ct,
        bool allowArchiveVideoTier, bool allowQuarantineFallback = false)
    {
        var name = upload.FileName.Trim();
        var maxBytes = EffectiveLimitFor(name, allowArchiveVideoTier);
        var maxLabel = maxBytes == MaxArchiveVideoBytes ? "100 MiB" : "20 MiB";
        if (upload.Length > maxBytes) throw new PayloadTooLargeException(maxBytes, $"File exceeds {maxLabel}.");
        if (name.Length is 0 or > 255 || name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c))
            || name.EndsWith('.') || upload.Length <= 0)
            throw Invalid($"File name or size is invalid (maximum {maxLabel}).");
        var extension = Path.GetExtension(name);
        if (!Types.TryGetValue(extension, out var mime) || !string.Equals(upload.ContentType, mime, StringComparison.OrdinalIgnoreCase))
            throw Invalid("File extension and MIME type must match a supported format.");
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int length;
        while ((length = await upload.Content.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + length > maxBytes) throw new PayloadTooLargeException(maxBytes, $"File exceeds {maxLabel}.");
            await output.WriteAsync(buffer.AsMemory(0, length), ct);
        }
        var bytes = output.ToArray();
        if (bytes.Length != upload.Length) throw Invalid("File size does not match the uploaded content.");
        if (!allowQuarantineFallback && !Matches(extension.ToLowerInvariant(), bytes))
            throw Invalid("File content does not match the declared format.");
        return new(name, mime, bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static bool Matches(string extension, byte[] bytes)
    {
        if (extension == ".pdf") return bytes.AsSpan().StartsWith("%PDF-"u8);
        if (extension == ".png") return bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (extension is ".jpg" or ".jpeg") return bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 });
        if (extension == ".mp4") return bytes.Length >= 8 && bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8);
        if (extension == ".txt")
        {
            try { return !new UTF8Encoding(false, true).GetString(bytes).Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')); }
            catch (DecoderFallbackException) { return false; }
        }
        try
        {
            using var content = new MemoryStream(bytes);
            using var archive = new ZipArchive(content, ZipArchiveMode.Read);
            if (archive.Entries.Count is 0 or > 10000) return false;
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.Length > 200L * 1024 * 1024 - expandedBytes) return false;
                expandedBytes += entry.Length;
            }
            var required = extension switch { ".docx" => "word/document.xml", ".xlsx" => "xl/workbook.xml",
                ".pptx" => "ppt/presentation.xml", _ => null };
            return required is null || (archive.GetEntry("[Content_Types].xml") is not null && archive.GetEntry(required) is not null
                && !archive.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)));
        }
        catch (InvalidDataException) { return false; }
    }

    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["file"] = [message] });
}
