using System.Buffers.Binary;
using System.IO.Compression;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Files;

namespace AIPMS.UnitTests.Application;

public sealed class ProjectFileUploadValidatorTests
{
    [Fact]
    public async Task Validates_a_20_MiB_pdf_on_disk_without_loading_its_contents_into_an_array()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aipms-validator-{Guid.NewGuid():N}");
        await using var disk = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await disk.WriteAsync("%PDF-1.7\n"u8.ToArray());
        disk.SetLength(UploadValidator.MaxBytes);
        using var bounded = new BoundedReadStream(disk);
        var result = await ProjectFileUploadValidator.ValidateAsync(bounded, "large.pdf", "application/pdf", disk.Length, default);
        Assert.Equal(UploadValidator.MaxBytes, result.Length);
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(0, disk.Position);
        Assert.True(bounded.ReadBytes >= UploadValidator.MaxBytes);
    }

    [Fact]
    public void Rejects_pdf_and_docx_above_20_MiB_with_PayloadTooLarge_per_POL06_while_allowing_zip_up_to_500_MiB()
    {
        var over20MiB = UploadValidator.MaxBytes + 1;
        var pdfEx = Assert.Throws<PayloadTooLargeException>(() =>
            ProjectFileUploadValidator.ValidateMetadata("report.pdf", "application/pdf", over20MiB));
        Assert.Equal(UploadValidator.MaxBytes, pdfEx.MaxBytes);

        var docxEx = Assert.Throws<PayloadTooLargeException>(() =>
            ProjectFileUploadValidator.ValidateMetadata("spec.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", over20MiB));
        Assert.Equal(UploadValidator.MaxBytes, docxEx.MaxBytes);

        // ZIP up to 500 MiB is permitted in metadata validation
        ProjectFileUploadValidator.ValidateMetadata("source.zip", "application/zip", UploadPolicies.ProjectFileLimit);
        var zipEx = Assert.Throws<PayloadTooLargeException>(() =>
            ProjectFileUploadValidator.ValidateMetadata("source.zip", "application/zip", UploadPolicies.ProjectFileLimit + 1));
        Assert.Equal(UploadPolicies.ProjectFileLimit, zipEx.MaxBytes);
    }

    [Theory]
    [InlineData("file.zip", "application/zip", 524288001)]
    [InlineData("file.pdf", "application/pdf", 25 * 1024 * 1024)]
    [InlineData("file.pdf", "application/pdf", 0)]
    [InlineData("../file.pdf", "application/pdf", 100)]
    [InlineData("file.pdf ", "application/pdf", 100)]
    [InlineData("file.pdf", "application/zip", 100)]
    [InlineData("file.exe", "application/octet-stream", 100)]
    [InlineData("file.txt", "text/plain", 100)]
    public void Rejects_oversize_and_invalid_metadata_before_reading_content(string name, string mime, long length)
    {
        Assert.ThrowsAny<Exception>(() => ProjectFileUploadValidator.ValidateMetadata(name, mime, length));
        if (name.EndsWith(".zip") && length > UploadPolicies.ProjectFileLimit)
            Assert.Equal(UploadPolicies.ProjectFileLimit,
                Assert.Throws<PayloadTooLargeException>(() => ProjectFileUploadValidator.ValidateMetadata(name, mime, length)).MaxBytes);
        if (name.EndsWith(".pdf") && length > UploadValidator.MaxBytes)
            Assert.Equal(UploadValidator.MaxBytes,
                Assert.Throws<PayloadTooLargeException>(() => ProjectFileUploadValidator.ValidateMetadata(name, mime, length)).MaxBytes);
    }

    [Fact]
    public async Task Rejects_content_spoofing_and_length_mismatch()
    {
        using var input = new MemoryStream("not a PDF"u8.ToArray());
        await Assert.ThrowsAsync<ValidationException>(() => ProjectFileUploadValidator.ValidateAsync(input, "file.pdf", "application/pdf", input.Length, default));
        await Assert.ThrowsAsync<ValidationException>(() => ProjectFileUploadValidator.ValidateAsync(input, "file.pdf", "application/pdf", input.Length + 1, default));
    }

    [Fact]
    public async Task Rejects_expanded_zip_bomb_from_directory_without_decompressing()
    {
        var bytes = Archive("large.dat");
        var central = FindSignature(bytes, 0x02014b50);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24), 0x80000001);
        using var input = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ValidationException>(() => ProjectFileUploadValidator.ValidateAsync(input, "file.zip", "application/zip", bytes.Length, default));
    }

    [Fact]
    public async Task Rejects_large_central_directory_before_allocating_zip_entries()
    {
        var bytes = Archive("data.txt");
        var end = FindSignature(bytes, 0x06054b50);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(end + 12), 20 * 1024 * 1024);
        using var input = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ValidationException>(() => ProjectFileUploadValidator.ValidateAsync(input, "file.zip", "application/zip", bytes.Length, default));
    }

    [Fact]
    public async Task Rejects_directory_headers_that_exceed_the_claimed_directory_size()
    {
        var bytes = Archive("data.txt");
        var end = FindSignature(bytes, 0x06054b50);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(end + 12), 46);
        using var input = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ValidationException>(() => ProjectFileUploadValidator.ValidateAsync(input, "file.zip", "application/zip", bytes.Length, default));
    }

    [Fact]
    public async Task Accepts_valid_zip_but_rejects_fake_docx_and_macros()
    {
        var bytes = Archive("data.txt");
        using var input = new MemoryStream(bytes);
        Assert.Equal(bytes.Length, (await ProjectFileUploadValidator.ValidateAsync(input, "file.zip", "application/zip", bytes.Length, default)).Length);
        const string docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        await Assert.ThrowsAsync<ValidationException>(() => ProjectFileUploadValidator.ValidateAsync(input, "file.docx", docx, bytes.Length, default));
        using var macro = new MemoryStream(Archive("[Content_Types].xml", "word/document.xml", "word/vbaProject.bin"));
        await Assert.ThrowsAsync<ValidationException>(() => ProjectFileUploadValidator.ValidateAsync(macro, "file.docx", docx, macro.Length, default));
        using var valid = new MemoryStream(Archive("[Content_Types].xml", "word/document.xml"));
        Assert.Equal(valid.Length, (await ProjectFileUploadValidator.ValidateAsync(valid, "file.docx", docx, valid.Length, default)).Length);
    }

    [Fact]
    public async Task Honors_cancellation_while_reading_staged_content()
    {
        using var input = new MemoryStream("%PDF-test"u8.ToArray());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectFileUploadValidator.ValidateAsync(input, "file.pdf", "application/pdf", input.Length, canceled.Token));
    }

    private static byte[] Archive(params string[] names)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in names)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write("example"u8);
            }
        return output.ToArray();
    }

    private static int FindSignature(byte[] bytes, uint signature)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == signature) return i;
        throw new InvalidOperationException("ZIP signature not found.");
    }

    private sealed class BoundedReadStream(Stream inner) : Stream
    {
        public long ReadBytes { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override int Read(byte[] buffer, int offset, int count)
        {
            Assert.InRange(count, 0, 131072);
            var read = inner.Read(buffer, offset, count); ReadBytes += read; return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Assert.InRange(buffer.Length, 0, 131072);
            var read = await inner.ReadAsync(buffer, ct); ReadBytes += read; return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
