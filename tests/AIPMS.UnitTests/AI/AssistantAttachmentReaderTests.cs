using System.IO.Compression;
using System.Text;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Infrastructure.Services;
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
}
