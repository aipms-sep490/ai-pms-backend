using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using UglyToad.PdfPig;

namespace AIPMS.Infrastructure.Services;

internal sealed record AssistantAttachment(string Name, string MediaType, string Checksum, string? Text, AiImageInput? Image);

internal static class AssistantAttachmentReader
{
    internal const int MaxFileBytes = 4 * 1024 * 1024;
    internal const int MaxTotalBytes = 8 * 1024 * 1024;
    private const int MaxTextLength = 20000;

    public static IReadOnlyList<AssistantAttachment> Read(IReadOnlyList<AssistantUpload> files, CancellationToken ct)
    {
        if (files.Count > 4 || files.Sum(f => (long)f.Content.Length) > MaxTotalBytes)
            throw new ArgumentException("Tối đa 4 tệp, tổng dung lượng tối đa 8 MB.");
        var result = new List<AssistantAttachment>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (file.Content.Length is 0 or > MaxFileBytes) throw new ArgumentException("Mỗi tệp phải có nội dung và không vượt quá 4 MB.");
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
                    case ".txt": case ".md": case ".csv": case ".json":
                        mediaType = "text/plain";
                        text = new UTF8Encoding(false, true).GetString(file.Content).TrimStart('\uFEFF');
                        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) throw new InvalidDataException();
                        break;
                    default: throw new ArgumentException("Hỗ trợ PNG, JPG, WebP, PDF, DOCX, TXT, Markdown, CSV và JSON.");
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
}
