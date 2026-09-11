using AIPMS.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aipms-storage-tests", Guid.NewGuid().ToString("N"));
    private LocalFileStorage Storage => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["FileStorage:RootPath"] = root }).Build());

    [Fact]
    public async Task Stored_bytes_survive_new_instance_and_cannot_be_overwritten()
    {
        var key = Guid.NewGuid().ToString("N");
        using var original = new MemoryStream("original"u8.ToArray());
        await Storage.WriteAsync(key, original, default);
        using var replacement = new MemoryStream("replacement"u8.ToArray());
        await Assert.ThrowsAsync<IOException>(() => Storage.WriteAsync(key, replacement, default));
        await using (var downloaded = await Storage.OpenReadAsync(key, default))
        using (var reader = new StreamReader(downloaded))
            Assert.Equal("original", await reader.ReadToEndAsync());
        await Storage.DeleteAsync(key, default);
        await Storage.DeleteAsync(key, default);
        await Assert.ThrowsAsync<FileNotFoundException>(() => Storage.OpenReadAsync(key, default));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("C:\\secret")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/")]
    [InlineData("")]
    public async Task Keys_cannot_escape_private_root(string key)
    {
        using var content = new MemoryStream("test"u8.ToArray());
        await Assert.ThrowsAsync<ArgumentException>(() => Storage.WriteAsync(key, content, default));
        await Assert.ThrowsAsync<ArgumentException>(() => Storage.OpenReadAsync(key, default));
        await Assert.ThrowsAsync<ArgumentException>(() => Storage.DeleteAsync(key, default));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Failed_write_removes_only_its_partial_object()
    {
        var key = Guid.NewGuid().ToString("N");
        using var content = new BrokenStream();
        await Assert.ThrowsAsync<IOException>(() => Storage.WriteAsync(key, content, default));
        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public async Task Cancelled_write_removes_partial_content()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var content = new MemoryStream("test"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Storage.WriteAsync(Guid.NewGuid().ToString("N"), content, cancellation.Token));
        Assert.Empty(Directory.GetFiles(root));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync("partial"u8.ToArray(), cancellationToken);
            throw new IOException("Simulated interrupted source");
        }
    }
}
