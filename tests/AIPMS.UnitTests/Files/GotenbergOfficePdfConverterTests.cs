using System.IO.Compression;
using System.Net;
using System.Text;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Services.Files;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIPMS.UnitTests.Files;

public sealed class GotenbergOfficePdfConverterTests : IDisposable
{
    private readonly string cacheDir = Path.Combine(Path.GetTempPath(), $"aipms-pdf-cache-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task Throws_service_unavailable_when_gotenberg_url_is_not_configured()
    {
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        using var sut = new GotenbergOfficePdfConverter(
            new GotenbergOfficePdfSettings("", 30, 20 * 1024 * 1024, cacheDir, 32 * 1024 * 1024, 2),
            http,
            NullLogger<GotenbergOfficePdfConverter>.Instance);

        Assert.False(sut.IsEnabled);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => sut.ConvertToPdfAsync("report.docx", BuildMinimalDocx()));
    }

    [Fact]
    public async Task Rejects_unsupported_extension_invalid_header_and_macro_packages()
    {
        using var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        using var sut = new GotenbergOfficePdfConverter(
            new GotenbergOfficePdfSettings("http://gotenberg:3000", 30, 20 * 1024 * 1024, cacheDir, 32 * 1024 * 1024, 2),
            http,
            NullLogger<GotenbergOfficePdfConverter>.Instance);

        await Assert.ThrowsAsync<ValidationException>(() => sut.ConvertToPdfAsync("image.png", [1, 2, 3, 4]));
        await Assert.ThrowsAsync<ValidationException>(() => sut.ConvertToPdfAsync("fake.docx", Encoding.UTF8.GetBytes("not a zip")));
        await Assert.ThrowsAsync<ValidationException>(() => sut.ConvertToPdfAsync("fake.doc", Encoding.UTF8.GetBytes("not ole2")));
        await Assert.ThrowsAsync<ValidationException>(() => sut.ConvertToPdfAsync("macro.docx", BuildMinimalDocx(includeMacro: true)));
    }

    [Fact]
    public async Task Converts_unicode_docx_with_ascii_filename_and_caches_by_sha256()
    {
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");
        var calls = 0;
        string? capturedForm = null;
        var handler = new FakeHandler(async (req, ct) =>
        {
            calls++;
            Assert.Equal("http://gotenberg:3000/forms/libreoffice/convert", req.RequestUri!.ToString());
            capturedForm = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(pdfBytes)
            };
        });
        using var http = new HttpClient(handler);
        using var sut = new GotenbergOfficePdfConverter(
            new GotenbergOfficePdfSettings("http://gotenberg:3000", 30, 20 * 1024 * 1024, cacheDir, 32 * 1024 * 1024, 2),
            http,
            NullLogger<GotenbergOfficePdfConverter>.Instance);

        var docx = BuildMinimalDocx();
        var first = await sut.ConvertToPdfAsync("MỞ BÀI.docx", docx);
        var second = await sut.ConvertToPdfAsync("Bản sao.docx", docx);

        Assert.Equal(pdfBytes, first);
        Assert.Equal(pdfBytes, second);
        Assert.Equal(1, calls);
        Assert.Contains("filename=document.docx", capturedForm, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Throws_service_unavailable_when_gotenberg_fails_or_returns_non_pdf()
    {
        using var http500 = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var sut500 = new GotenbergOfficePdfConverter(
            new GotenbergOfficePdfSettings("http://gotenberg:3000", 30, 20 * 1024 * 1024, cacheDir, 32 * 1024 * 1024, 2),
            http500,
            NullLogger<GotenbergOfficePdfConverter>.Instance);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => sut500.ConvertToPdfAsync("report.docx", BuildMinimalDocx()));

        using var httpBad = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>oops</html>") }));
        using var sutBad = new GotenbergOfficePdfConverter(
            new GotenbergOfficePdfSettings("http://gotenberg:3000", 30, 20 * 1024 * 1024, cacheDir, 32 * 1024 * 1024, 2),
            httpBad,
            NullLogger<GotenbergOfficePdfConverter>.Instance);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => sutBad.ConvertToPdfAsync("report.docx", BuildMinimalDocx()));
    }

    private static byte[] BuildMinimalDocx(bool includeMacro = false)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open()))
                w.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
                w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Xin chào</w:t></w:r></w:p></w:body></w:document>");
            if (includeMacro)
            {
                using var w = new StreamWriter(zip.CreateEntry("word/vbaProject.bin").Open());
                w.Write("macro");
            }
        }
        return ms.ToArray();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true); } catch { }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> syncRespond) => respond = (r, _) => Task.FromResult(syncRespond(r));
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> asyncRespond) => respond = asyncRespond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
