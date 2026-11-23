using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.Files;
using ClosedXML.Excel;
using UglyToad.PdfPig;

namespace AIPMS.Infrastructure.Services;

internal sealed record AssistantAttachment(string Name, string MediaType, string Checksum, string? Text, AiImageInput? Image);

internal static class AssistantAttachmentReader
{
    internal const int MaxFiles = UploadPolicies.AcademicAssistantMaxFiles;
    internal const int MaxFileBytes = (int)UploadPolicies.AcademicAssistantFileLimit;
    internal const int MaxTotalBytes = (int)UploadPolicies.AcademicAssistantTotalLimit;
    private const int MaxTextLength = 20000;
    private const int MaxXlsxSheets = 5;
    private const int MaxXlsxRowsPerSheet = 300;
    private const int MaxXlsxColsPerRow = 30;
    private const long MaxXlsxExpandedBytes = 16L * 1024 * 1024;

    public static IReadOnlyList<AssistantAttachment> Read(IReadOnlyList<AssistantUpload> files, CancellationToken ct)
    {
        if (files.Count > MaxFiles || files.Sum(f => (long)f.Content.Length) > MaxTotalBytes)
            throw new ArgumentException("Tối đa 4 tệp, mỗi tệp tối đa 10 MB và tổng dung lượng tối đa 20 MB.");
        var result = new List<AssistantAttachment>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (file.Content.Length is 0 or > MaxFileBytes) throw new ArgumentException("Mỗi tệp phải có nội dung và không vượt quá 10 MB.");
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) throw new ArgumentException("Tên tệp không hợp lệ.");
            var extension = Path.GetExtension(name).ToLowerInvariant();
            string? text = null;
            AiImageInput? image = null;
            string mediaType;
            try
            {
                switch (extension)
                {
                    case ".png":
                        if (!file.Content.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException();
                        mediaType = "image/png"; image = new(mediaType, file.Content); break;
                    case ".jpg": case ".jpeg":
                        if (!file.Content.AsSpan().StartsWith(new byte[] { 255, 216, 255 })) throw new InvalidDataException();
                        mediaType = "image/jpeg"; image = new(mediaType, file.Content); break;
                    case ".webp":
                        if (file.Content.Length < 12 || Encoding.ASCII.GetString(file.Content, 0, 4) != "RIFF" || Encoding.ASCII.GetString(file.Content, 8, 4) != "WEBP") throw new InvalidDataException();
                        mediaType = "image/webp"; image = new(mediaType, file.Content); break;
                    case ".pdf":
                        if (!file.Content.AsSpan().StartsWith("%PDF-"u8)) throw new InvalidDataException();
                        mediaType = "application/pdf";
                        using (var pdf = PdfDocument.Open(file.Content))
                        {
                            if (pdf.NumberOfPages > 60) throw new ArgumentException("PDF tối đa 60 trang.");
                            var content = new StringBuilder();
                            foreach (var page in pdf.GetPages())
                            {
                                ct.ThrowIfCancellationRequested();
                                content.AppendLine($"[Trang {page.Number}] {page.Text}");
                                if (content.Length > MaxTextLength) throw new ArgumentException("Tài liệu quá dài; hãy gửi phần cần hỏi (tối đa 20.000 ký tự mỗi tệp).");
                            }
                            text = content.ToString();
                            if (!pdf.GetPages().Any(p => !string.IsNullOrWhiteSpace(p.Text)))
                                throw new ArgumentException("PDF chưa có lớp văn bản. Hãy gửi ảnh trang cần đọc; hiện chưa hỗ trợ OCR toàn bộ PDF scan.");
                        }
                        break;
                    case ".docx":
                        mediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                        using (var stream = new MemoryStream(file.Content))
                        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                        {
                            if (zip.Entries.Count > 1000 || zip.Entries.Sum(e => e.Length) > 16 * 1024 * 1024) throw new InvalidDataException();
                            var document = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException();
                            if (document.Length > 2 * 1024 * 1024) throw new InvalidDataException();
                            using var input = document.Open();
                            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
                            var xml = XDocument.Load(reader);
                            XNamespace ns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                            text = string.Join("\n", xml.Descendants(ns + "p").Select(p => string.Concat(p.Descendants(ns + "t").Select(t => t.Value))));
                        }
                        break;
                    case ".xlsx":
                        mediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                        text = ExtractXlsx(file.Content, ct);
                        break;
                    case ".txt": case ".md": case ".csv": case ".json":
                        mediaType = "text/plain";
                        text = new UTF8Encoding(false, true).GetString(file.Content).TrimStart('\uFEFF');
                        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) throw new InvalidDataException();
                        break;
                    default: throw new ArgumentException("Hỗ trợ PNG, JPG, WebP, PDF, DOCX, XLSX, TXT, Markdown, CSV và JSON.");
                }
            }
            catch (Exception ex) when (ex is not (ArgumentException or OperationCanceledException))
            { throw new ArgumentException("Không đọc được tệp. Kiểm tra định dạng, mã hóa UTF-8 hoặc mật khẩu bảo vệ."); }
            if (text is not null && (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength))
                throw new ArgumentException("Tệp phải có văn bản và không vượt quá 20.000 ký tự. Hãy gửi phần cần hỏi.");
            result.Add(new(name, mediaType, Convert.ToHexString(SHA256.HashData(file.Content)), text, image));
        }
        if (result.Sum(r => r.Text?.Length ?? 0) > 40000) throw new ArgumentException("Tổng nội dung tài liệu tối đa 40.000 ký tự.");
        return result;
    }

    internal static string ExtractXlsx(byte[] content, CancellationToken ct)
    {
        if (!IsSafeXlsxPackage(content))
            throw new ArgumentException("Tệp XLSX không hợp lệ hoặc chứa macro/liên kết ngoài không được hỗ trợ.");

        using var stream = new MemoryStream(content, writable: false);
        using var workbook = new XLWorkbook(stream);
        var builder = new StringBuilder();
        var sheetsRead = 0;

        foreach (var sheet in workbook.Worksheets)
        {
            ct.ThrowIfCancellationRequested();
            if (sheetsRead >= MaxXlsxSheets) break;
            var used = sheet.RangeUsed();
            if (used is null) continue;
            sheetsRead++;

            if (builder.Length > 0) builder.AppendLine();
            builder.AppendLine($"[Sheet: {sheet.Name}]");

            var firstRow = used.FirstRow().RowNumber();
            var lastRow = Math.Min(used.LastRow().RowNumber(), firstRow + MaxXlsxRowsPerSheet - 1);
            var firstCol = used.FirstColumn().ColumnNumber();
            var lastCol = Math.Min(used.LastColumn().ColumnNumber(), firstCol + MaxXlsxColsPerRow - 1);

            for (var r = firstRow; r <= lastRow; r++)
            {
                ct.ThrowIfCancellationRequested();
                var cells = new List<string>(lastCol - firstCol + 1);
                var hasValue = false;
                for (var c = firstCol; c <= lastCol; c++)
                {
                    var cell = sheet.Cell(r, c);
                    var raw = cell.HasFormula ? cell.CachedValue.ToString() : cell.GetFormattedString();
                    var cleaned = SanitizeCell(raw);
                    if (!string.IsNullOrEmpty(cleaned)) hasValue = true;
                    cells.Add(cleaned);
                }
                if (!hasValue) continue;
                while (cells.Count > 0 && string.IsNullOrEmpty(cells[^1])) cells.RemoveAt(cells.Count - 1);
                var line = string.Join(" | ", cells);
                if (builder.Length + line.Length + 60 > MaxTextLength)
                {
                    builder.AppendLine("...[Nội dung bảng Excel được rút gọn theo giới hạn 20.000 ký tự]");
                    return builder.ToString().Trim();
                }
                builder.AppendLine(line);
            }
        }

        return builder.ToString().Trim();
    }

    private static bool IsSafeXlsxPackage(byte[] bytes)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            if (archive.Entries.Count is < 2 or > 200 || archive.GetEntry("xl/workbook.xml") is null) return false;
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                expanded += entry.Length;
                if (expanded > MaxXlsxExpandedBytes
                    || entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static string SanitizeCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
    }
}
