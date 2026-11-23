using System.IO.Compression;
using System.Text;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Infrastructure.Services;
using ClosedXML.Excel;
using Xunit;

namespace AIPMS.UnitTests.AI;

public sealed class AssistantAttachmentReaderTests
{
    [Fact]
    public void ReadsUtf8TextAndSanitizesPath()
    {
        var file = Assert.Single(AssistantAttachmentReader.Read([new("../../note.txt", Encoding.UTF8.GetBytes("Xin chào"))], default));
        Assert.Equal("note.txt", file.Name);
        Assert.Equal("Xin chào", file.Text);
        Assert.Null(file.Image);
        Assert.Equal(64, file.Checksum.Length);
    }

    [Theory]
    [InlineData("fake.png", "plain text")]
    [InlineData("fake.pdf", "plain text")]
    [InlineData("fake.docx", "plain text")]
    [InlineData("run.exe", "binary")]
    [InlineData("bad.txt", "\0")]
    public void RejectsSpoofedAndUnsupportedFiles(string name, string content) =>
        Assert.Throws<ArgumentException>(() => AssistantAttachmentReader.Read([new(name, Encoding.UTF8.GetBytes(content))], default));

    [Fact]
    public void RejectsOversizedAndTooManyAttachments()
    {
        Assert.Throws<ArgumentException>(() => AssistantAttachmentReader.Read([new("large.txt", new byte[AssistantAttachmentReader.MaxFileBytes + 1])], default));
        Assert.Throws<ArgumentException>(() => AssistantAttachmentReader.Read(Enumerable.Range(0, 5).Select(i => new AssistantUpload($"{i}.txt", [65])).ToArray(), default));
        Assert.Throws<ArgumentException>(() => AssistantAttachmentReader.Read([new("long.txt", Encoding.UTF8.GetBytes(new string('a', 20001)))], default));
    }

    [Fact]
    public void AcceptsConfiguredFileAndRequestBoundaries()
    {
        static byte[] Png(int length)
        {
            var bytes = new byte[length];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
            return bytes;
        }

        var files = new[]
        {
            new AssistantUpload("first.png", Png(AssistantAttachmentReader.MaxFileBytes)),
            new AssistantUpload("second.png", Png(AssistantAttachmentReader.MaxFileBytes))
        };

        var result = AssistantAttachmentReader.Read(files, default);
        Assert.Equal(2, result.Count);
        Assert.All(result, attachment => Assert.Equal("image/png", attachment.MediaType));
    }

    [Fact]
    public void ExtractsDocxParagraphsWithoutFollowingExternalEntities()
    {
        byte[] Docx(string xml)
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open())) writer.Write(xml);
            return stream.ToArray();
        }
        var bytes = Docx("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Project memo</w:t></w:r></w:p></w:body></w:document>""");
        Assert.Equal("Project memo", Assert.Single(AssistantAttachmentReader.Read([new("memo.docx", bytes)], default)).Text);
        var hostile = Docx("""<!DOCTYPE x [<!ENTITY secret SYSTEM "file:///private">]><x>&secret;</x>""");
        Assert.Throws<ArgumentException>(() => AssistantAttachmentReader.Read([new("bad.docx", hostile)], default));
    }

    [Fact]
    public void ExtractsXlsxSheetsSafelyAndRejectsMacrosOrCorruptPackages()
    {
        byte[] BuildValidXlsx()
        {
            using var book = new XLWorkbook();
            var sheet = book.Worksheets.Add("Tiêu chí");
            sheet.Cell("A1").Value = "Tên tiêu chí";
            sheet.Cell("B1").Value = "Trọng số";
            sheet.Cell("A2").Value = "Kiến trúc hệ thống";
            sheet.Cell("B2").Value = 40;
            using var ms = new MemoryStream();
            book.SaveAs(ms);
            return ms.ToArray();
        }

        var valid = Assert.Single(AssistantAttachmentReader.Read([new("rubric.xlsx", BuildValidXlsx())], default));
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", valid.MediaType);
        Assert.Contains("[Sheet: Tiêu chí]", valid.Text);
        Assert.Contains("Kiến trúc hệ thống | 40", valid.Text);

        // Reject spoofed/non-zip xlsx
        Assert.Throws<ArgumentException>(() => AssistantAttachmentReader.Read([new("fake.xlsx", Encoding.UTF8.GetBytes("not a zip"))], default));

        // Reject xlsx containing vbaProject macro payload
        using var macroStream = new MemoryStream();
        using (var zip = new ZipArchive(macroStream, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open())) w.Write("<workbook/>");
            using (var w = new StreamWriter(zip.CreateEntry("xl/vbaProject.bin").Open())) w.Write("macro");
        }
        Assert.Throws<ArgumentException>(() => AssistantAttachmentReader.Read([new("macro.xlsx", macroStream.ToArray())], default));
    }
}
