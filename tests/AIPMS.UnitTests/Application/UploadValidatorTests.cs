using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;

namespace AIPMS.UnitTests.Application;

public sealed class UploadValidatorTests
{
    private static async Task<ValidatedUpload> Read(byte[] bytes, string name = "evidence.txt", string mime = "text/plain", long? size = null)
    {
        using var content = new MemoryStream(bytes);
        return await UploadValidator.ReadAsync(new(name, mime, size ?? bytes.Length, content), default);
    }

    [Fact]
    public async Task Valid_utf8_preserves_bytes_and_computes_checksum()
    {
        var bytes = Encoding.UTF8.GetBytes("Evidence\r\nLine two\tOK");
        var result = await Read(bytes, " evidence.TXT ");
        Assert.Equal(bytes, result.Bytes);
        Assert.Equal("evidence.TXT", result.FileName);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Sha256);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20 * 1024 * 1024 + 1)]
    [InlineData(5)]
    public async Task Invalid_declared_size_is_rejected(long size) =>
        await Assert.ThrowsAsync<ValidationException>(() => Read("text"u8.ToArray(), size: size));

    [Fact]
    public async Task Actual_size_is_bounded_even_when_client_declares_a_small_file()
    {
        var bytes = new byte[UploadValidator.MaxBytes + 1];
        Array.Fill(bytes, (byte)'a');
        await Assert.ThrowsAsync<ValidationException>(() => Read(bytes, size: 1));
        Assert.Equal(UploadValidator.MaxBytes, (await Read(bytes[..UploadValidator.MaxBytes])).Bytes.Length);
    }

    [Theory]
    [InlineData("../file.txt")]
    [InlineData("..\\file.txt")]
    [InlineData("file.txt:stream")]
    [InlineData("file\r\n.txt")]
    [InlineData("file.txt.")]
    [InlineData("file.exe")]
    public async Task Unsafe_names_or_unsupported_extensions_are_rejected(string name) =>
        await Assert.ThrowsAsync<ValidationException>(() => Read("text"u8.ToArray(), name));

    [Theory]
    [InlineData("evidence.pdf", "application/pdf")]
    [InlineData("evidence.png", "image/png")]
    [InlineData("evidence.jpg", "image/jpeg")]
    [InlineData("evidence.zip", "application/zip")]
    [InlineData("evidence.txt", "application/pdf")]
    public async Task Mismatched_mime_or_signature_is_rejected(string name, string mime) =>
        await Assert.ThrowsAsync<ValidationException>(() => Read("fake"u8.ToArray(), name, mime));

    [Theory]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 65, 0, 66 })]
    public async Task Text_must_be_utf8_without_binary_controls(byte[] bytes) =>
        await Assert.ThrowsAsync<ValidationException>(() => Read(bytes));

    [Theory]
    [InlineData("docx", "word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("xlsx", "xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("pptx", "ppt/presentation.xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    public async Task Office_requires_expected_parts_and_rejects_macro_payloads(string extension, string part, string mime)
    {
        var bytes = Zip("[Content_Types].xml", part);
        Assert.Equal(bytes, (await Read(bytes, $"report.{extension}", mime)).Bytes);
        await Assert.ThrowsAsync<ValidationException>(() => Read(Zip("random.txt"), $"report.{extension}", mime));
        await Assert.ThrowsAsync<ValidationException>(() => Read(Zip("[Content_Types].xml", part, "word/vbaProject.bin"), $"report.{extension}", mime));
    }

    [Fact]
    public async Task Zip_entry_limit_is_enforced()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Read(Zip(Enumerable.Range(0, 10001).Select(i => $"{i}.txt").ToArray()), "evidence.zip", "application/zip"));
    }

    private static byte[] Zip(params string[] names)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in names)
            {
                using var entry = new StreamWriter(archive.CreateEntry(name).Open());
                entry.Write("test");
            }
        return content.ToArray();
    }
}
