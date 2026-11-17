using System.IO.Compression;
using System.Text;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Services.Projects;
using ClosedXML.Excel;

namespace AIPMS.UnitTests.Application;

public sealed class OfficePreviewTests
{
    [Fact]
    public void Docx_returns_plain_text_without_executing_markup()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using (var content = zip.CreateEntry("[Content_Types].xml").Open())
                content.Write(Encoding.UTF8.GetBytes("<Types/>"));
            using (var document = zip.CreateEntry("word/document.xml").Open())
                document.Write(Encoding.UTF8.GetBytes("""
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Xin chào &amp; hướng dẫn</w:t></w:r></w:p></w:body></w:document>
                    """));
        }
        var result = new OfficePreviewService().Preview("template.docx", bytes.ToArray());
        Assert.Equal("DOCX_TEXT", result.Kind);
        Assert.Equal("Xin chào & hướng dẫn", Assert.Single(result.Paragraphs));
    }

    [Fact]
    public void Xlsx_returns_bounded_readonly_grid()
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add("Tiêu chí");
        sheet.Cell("A1").Value = "Gate 1";
        sheet.Cell("B1").Value = 50;
        using var bytes = new MemoryStream();
        book.SaveAs(bytes);
        var result = new OfficePreviewService().Preview("rubric.xlsx", bytes.ToArray());
        Assert.Equal("XLSX_GRID", result.Kind);
        Assert.Equal("Tiêu chí", Assert.Single(result.Sheets).Name);
        Assert.Equal(["Gate 1", "50"], result.Sheets[0].Rows[0]);
    }

    [Fact]
    public void Rejects_oversized_and_unsupported_files()
    {
        var service = new OfficePreviewService();
        Assert.Throws<ValidationException>(() => service.Preview("a.docx", new byte[OfficePreviewService.MaxBytes + 1]));
        Assert.Throws<ValidationException>(() => service.Preview("a.xls", [1, 2, 3]));
    }

    [Fact]
    public void Rejects_docx_dtd_and_xlsx_external_links()
    {
        static byte[] Package(string entryName, string body)
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                using (var type = zip.CreateEntry("[Content_Types].xml").Open()) type.Write(Encoding.UTF8.GetBytes("<Types/>"));
                using (var entry = zip.CreateEntry(entryName).Open()) entry.Write(Encoding.UTF8.GetBytes(body));
            }
            return bytes.ToArray();
        }
        var service = new OfficePreviewService();
        Assert.Throws<ValidationException>(() => service.Preview("unsafe.docx",
            Package("word/document.xml", "<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///secret'>]><root>&x;</root>")));
        Assert.Throws<ValidationException>(() => service.Preview("unsafe.xlsx",
            Package("xl/externalLinks/externalLink1.xml", "<link/>")));
    }
}
