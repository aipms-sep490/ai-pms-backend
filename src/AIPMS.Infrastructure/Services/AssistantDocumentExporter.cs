using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AIPMS.Application.Common.Documents;
using AIPMS.Application.Features.AcademicKnowledge;
using ClosedXML.Excel;

namespace AIPMS.Infrastructure.Services;

internal static class AssistantDocumentExporter
{
    internal static AssistantExport Create(AcademicAssistantResponseDto result, string format)
    {
        var content = new StringBuilder(result.Answer).AppendLine().AppendLine().AppendLine("Nguồn và lưu ý")
            .AppendLine("Nội dung do trợ lý AI hỗ trợ. Kiểm tra trước khi sử dụng.").AppendLine(result.LimitationNote);
        foreach (var source in result.Citations)
            content.AppendLine($"{source.Title} · v{source.Version} · {source.Section}{(source.Page.HasValue ? " · trang " + source.Page : "")}: {source.Excerpt}");
        foreach (var source in result.ProjectEvidence) content.AppendLine($"{source.Title} · {source.PeriodOrDate}: {source.Excerpt}");
        foreach (var file in result.Attachments ?? []) content.AppendLine($"Tệp tham chiếu: {file.FileName}");
        var text = content.ToString();
        var lines = text.Replace("\r", "").Split('\n').Select(Plain).ToArray();
        return format switch
        {
            "txt" or "md" => new($"tro-ly-ai.{format}", "text/plain; charset=utf-8", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(format == "md" ? text : string.Join("\n", lines))).ToArray()),
            "docx" => new("tai-lieu-tro-ly-ai.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Word(lines)),
            "xlsx" => new("noi-dung-tro-ly-ai.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Workbook(lines)),
            "pdf" => new("tai-lieu-tro-ly-ai.pdf", "application/pdf", UnicodePdf.Write("Tài liệu trợ lý AI", lines)),
            _ => throw new ArgumentException("Unsupported document format.")
        };
    }

    private static string Plain(string text) => Regex.Replace(text, @"^#{1,6}\s+", "").Replace("**", "").Replace("`", "");

    private static byte[] Word(string[] lines)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Xml(string path, XElement root) { using var entry = archive.CreateEntry(path).Open(); new XDocument(root).Save(entry); }
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
            XElement Paragraph(string value, bool title = false) => new(w + "p",
                new XElement(w + "pPr", title ? new XElement(w + "pStyle", new XAttribute(w + "val", "Title")) : null,
                    new XElement(w + "spacing", new XAttribute(w + "after", "120"))),
                new XElement(w + "r", new XElement(w + "rPr", new XElement(w + "rFonts", new XAttribute(w + "ascii", "Calibri"), new XAttribute(w + "hAnsi", "Calibri")),
                    new XElement(w + "sz", new XAttribute(w + "val", title ? "32" : "22"))),
                    new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
            Xml("[Content_Types].xml", new XElement(ct + "Types",
                new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"))));
            Xml("_rels/.rels", new XElement(rel + "Relationships", new XElement(rel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "word/document.xml"))));
            Xml("word/_rels/document.xml.rels", new XElement(rel + "Relationships", new XElement(rel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml"))));
            Xml("word/styles.xml", new XElement(w + "styles", new XElement(w + "style", new XAttribute(w + "type", "paragraph"), new XAttribute(w + "styleId", "Title"), new XElement(w + "name", new XAttribute(w + "val", "Title")))));
            Xml("word/document.xml", new XElement(w + "document", new XElement(w + "body", Paragraph("Tài liệu trợ lý AI", true), lines.Select(line => Paragraph(line)),
                new XElement(w + "sectPr", new XElement(w + "pgSz", new XAttribute(w + "w", "12240"), new XAttribute(w + "h", "15840")),
                    new XElement(w + "pgMar", new XAttribute(w + "top", "1080"), new XAttribute(w + "bottom", "1080"), new XAttribute(w + "left", "1080"), new XAttribute(w + "right", "1080"), new XAttribute(w + "header", "360"), new XAttribute(w + "footer", "360"), new XAttribute(w + "gutter", "0"))))));
        }
        return output.ToArray();
    }

    private static byte[] Workbook(string[] lines)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Nội dung");
        sheet.Cell(1, 1).Value = "Mục"; sheet.Cell(1, 2).Value = "Nội dung trợ lý AI";
        sheet.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#123F35");
        sheet.Range(1, 1, 1, 2).Style.Font.FontColor = XLColor.White;
        sheet.Range(1, 1, 1, 2).Style.Font.Bold = true;
        var row = 2;
        foreach (var line in lines.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            // Assign text values, never formulas from model or document content.
            sheet.Cell(row, 1).Value = row - 1; sheet.Cell(row, 2).Value = line;
            sheet.Row(row).Height = Math.Min(400, 20 * (Math.Ceiling(line.Length / 80d) + 1)); row++;
        }
        sheet.Column(1).Width = 8; sheet.Column(2).Width = 100;
        sheet.Column(2).Style.Alignment.WrapText = true;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.SheetView.FreezeRows(1);
        using var output = new MemoryStream(); book.SaveAs(output); return output.ToArray();
    }
}
