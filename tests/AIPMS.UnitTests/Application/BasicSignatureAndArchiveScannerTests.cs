using System.IO.Compression;
using System.Text;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Infrastructure.Services.Files;
using AIPMS.Infrastructure.Services.Projects;

namespace AIPMS.UnitTests.Application;

public sealed class BasicSignatureAndArchiveScannerTests
{
    private readonly BasicSignatureAndArchiveScanner scanner = new();

    private static MemoryStream MakeZip(params (string Name, byte[] Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(content, 0, content.Length);
            }
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Accepts_valid_pdf_png_mp4_pptx_and_zip()
    {
        using var pdf = new MemoryStream("%PDF-1.7\nSafe content"u8.ToArray());
        Assert.Equal(FileScanStatuses.Ready, (await scanner.ScanAsync(pdf, "report.pdf", "application/pdf", default)).Status);

        using var png = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 });
        Assert.Equal(FileScanStatuses.Ready, (await scanner.ScanAsync(png, "diagram.png", "image/png", default)).Status);

        using var mp4 = new MemoryStream([0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2', 0, 0, 0, 0]);
        Assert.Equal(FileScanStatuses.Ready, (await scanner.ScanAsync(mp4, "demo.mp4", "video/mp4", default)).Status);

        using var pptx = MakeZip(
            ("[Content_Types].xml", "<Types/>"u8.ToArray()),
            ("ppt/presentation.xml", "<presentation/>"u8.ToArray()),
            ("ppt/slides/slide1.xml", "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:t>Hello Capstone</a:t></p:sld>"u8.ToArray()));
        Assert.Equal(FileScanStatuses.Ready, (await scanner.ScanAsync(pptx, "deck.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", default)).Status);

        // Verify OfficePreviewService handles PPTX slides & ZIP tree safely
        var previewer = new OfficePreviewService();
        var slidePreview = previewer.Preview("deck.pptx", pptx.ToArray());
        Assert.Equal("PPTX_SLIDES", slidePreview.Kind);
        Assert.Contains("Hello Capstone", Assert.Single(slidePreview.Paragraphs));

        using var zip = MakeZip(("src/app.ts", "export const x = 1;"u8.ToArray()), ("README.md", "# Demo"u8.ToArray()));
        var zipPreview = previewer.Preview("source.zip", zip.ToArray());
        Assert.Equal("ZIP_TREE", zipPreview.Kind);
        Assert.Equal(2, zipPreview.ZipEntries!.Count);
    }

    [Fact]
    public async Task Quarantines_disguised_executable_office_macro_path_traversal_and_zip_bomb()
    {
        // Disguised PE executable renamed to .pdf
        using var fakePdf = new MemoryStream(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });
        var exeRes = await scanner.ScanAsync(fakePdf, "invoice.pdf", "application/pdf", default);
        Assert.Equal(FileScanStatuses.Quarantined, exeRes.Status);
        Assert.False(exeRes.IsSafe);

        // Office DOCX containing vbaProject.bin macro
        using var macroDocx = MakeZip(
            ("[Content_Types].xml", "<Types/>"u8.ToArray()),
            ("word/document.xml", "<w:document/>"u8.ToArray()),
            ("word/vbaProject.bin", "macro-payload"u8.ToArray()));
        var macroRes = await scanner.ScanAsync(macroDocx, "spec.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", default);
        Assert.Equal(FileScanStatuses.Quarantined, macroRes.Status);

        // Path traversal ZIP
        using var traversalZip = MakeZip(("../evil.txt", "boom"u8.ToArray()));
        var travRes = await scanner.ScanAsync(traversalZip, "archive.zip", "application/zip", default);
        Assert.Equal(FileScanStatuses.Quarantined, travRes.Status);

        // Zip-bomb (2 MB of zeros compresses to ~2 KB => ratio > 100:1)
        using var bombZip = MakeZip(("zeros.bin", new byte[2 * 1024 * 1024]));
        var bombRes = await scanner.ScanAsync(bombZip, "bomb.zip", "application/zip", default);
        Assert.Equal(FileScanStatuses.Quarantined, bombRes.Status);
    }
}
