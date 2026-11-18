using AIPMS.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class GoogleDriveStorageTransferTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private readonly string spoolRoot = Path.Combine(Path.GetTempPath(), "aipms-drive-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1800, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    public void Transfer_timeout_is_bounded_and_old_configuration_remains_valid(int timeout, bool valid)
    {
        var values = new Dictionary<string, string?>
        {
            ["GoogleDrive:ClientId"] = "id", ["GoogleDrive:ClientSecret"] = "secret",
            ["GoogleDrive:RefreshToken"] = "refresh", ["GoogleDrive:FolderId"] = "folder-id",
            ["GoogleDrive:TimeoutSeconds"] = "30"
        };
        var oldSettings = GoogleDriveSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.True(oldSettings.IsValid);
        Assert.Equal(30, oldSettings.TimeoutSeconds);
        Assert.Equal(1800, oldSettings.TransferTimeoutSeconds);
        values["GoogleDrive:TransferTimeoutSeconds"] = timeout.ToString();
        var settings = GoogleDriveSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.Equal(valid, settings.IsValid);
        Assert.Equal(timeout, settings.TransferTimeoutSeconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfers_can_outlast_the_metadata_deadline(bool download)
    {
        var client = new TransferClient { Exists = download, TransferDelay = TimeSpan.FromMilliseconds(1300) };
        using var storage = new GoogleDriveFileStorage(Settings(metadata: 1, transfer: 10), client);
        if (download)
        {
            await using var content = await storage.OpenReadAsync(Key, default);
            Assert.Equal(7, content.ReadByte());
        }
        else
        {
            using var content = new MemoryStream([7]);
            await storage.WriteAsync(Key, content, default);
        }
        Assert.Equal(1, client.TransferCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfers_stop_at_their_own_deadline_without_retry(bool download)
    {
        var client = new TransferClient { Exists = download, TransferDelay = Timeout.InfiniteTimeSpan };
        using var storage = new GoogleDriveFileStorage(Settings(metadata: 30, transfer: 1), client);
        using var content = new MemoryStream([7]);
        var error = await Assert.ThrowsAsync<IOException>(async () =>
        {
            if (download) await storage.OpenReadAsync(Key, default);
            else await storage.WriteAsync(Key, content, default);
        });
        Assert.Contains("timed out", error.Message);
        Assert.Equal(1, client.TransferCalls);
        if (download) AssertSpoolRemoved(client);
    }

    [Fact]
    public async Task Large_download_is_disk_backed_seekable_and_removed_when_disposed()
    {
        const long size = 500L * 1024 * 1024;
        var client = new TransferClient { Exists = true, DownloadBytes = size };
        using var storage = new GoogleDriveFileStorage(Settings(), client);
        var content = await storage.OpenReadAsync(Key, default);
        try
        {
            Assert.IsAssignableFrom<FileStream>(content);
            Assert.Equal(spoolRoot, Path.GetDirectoryName(client.Output!.Name));
            Assert.Equal(size, content.Length);
            Assert.Equal(0, content.Position);
            Assert.Equal(7, content.ReadByte());
            content.Seek(-1, SeekOrigin.End);
            Assert.Equal(7, content.ReadByte());
        }
        finally { await content.DisposeAsync(); }
        AssertSpoolRemoved(client);
    }

    [Fact]
    public async Task Failed_download_removes_partial_spool()
    {
        var client = new TransferClient { Exists = true, FailDownload = true };
        using var storage = new GoogleDriveFileStorage(Settings(), client);
        await Assert.ThrowsAsync<IOException>(() => storage.OpenReadAsync(Key, default));
        AssertSpoolRemoved(client);
    }

    [Fact]
    public async Task Caller_cancelled_download_removes_spool_and_preserves_cancellation()
    {
        var client = new TransferClient { Exists = true, TransferDelay = Timeout.InfiniteTimeSpan };
        using var storage = new GoogleDriveFileStorage(Settings(), client);
        using var cancellation = new CancellationTokenSource();
        var download = storage.OpenReadAsync(Key, cancellation.Token);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        AssertSpoolRemoved(client);
    }

    private static void AssertSpoolRemoved(TransferClient client)
    {
        Assert.NotNull(client.Output);
        Assert.False(client.Output.CanWrite);
        Assert.False(File.Exists(client.Output.Name));
    }

    [Fact]
    public void Spool_defaults_to_private_persistent_storage_not_temporary_ram_disk()
    {
        var settings = GoogleDriveSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["FileStorage:RootPath"] = spoolRoot }).Build());
        Assert.Equal(Path.Combine(spoolRoot, ".downloads"), settings.DownloadSpoolPath);
        Assert.Equal(4, settings.MaxConcurrentDownloads);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public void Concurrent_download_configuration_is_bounded(int slots, bool valid)
        => Assert.Equal(valid, Settings(slots: slots).IsValid);

    [Fact]
    public async Task Download_slots_stay_reserved_until_response_stream_is_closed()
    {
        var client = new TransferClient { Exists = true };
        using var storage = new GoogleDriveFileStorage(Settings(metadata: 1, slots: 1), client);
        var first = await storage.OpenReadAsync(Key, default);
        await Assert.ThrowsAsync<IOException>(() => storage.OpenReadAsync(Key, default));
        Assert.Equal(1, client.TransferCalls);
        await first.DisposeAsync();
        await using var second = await storage.OpenReadAsync(Key, default);
        Assert.Equal(2, client.TransferCalls);
    }

    [Fact]
    public async Task Oversize_remote_object_is_rejected_and_partial_spool_is_removed()
    {
        var client = new TransferClient { Exists = true, Oversize = true };
        using var storage = new GoogleDriveFileStorage(Settings(), client);
        await Assert.ThrowsAsync<IOException>(() => storage.OpenReadAsync(Key, default));
        AssertSpoolRemoved(client);
    }

    [Fact]
    public async Task Startup_removes_crashed_spools_and_preserves_active_and_unrelated_files()
    {
        Directory.CreateDirectory(spoolRoot);
        var stale = Path.Combine(spoolRoot, $"aipms-drive-{Guid.NewGuid():N}.tmp");
        var unrelated = Path.Combine(spoolRoot, "aipms-drive-other.tmp");
        await File.WriteAllTextAsync(stale, "crashed");
        await File.WriteAllTextAsync(unrelated, "unrelated");
        var client = new TransferClient { Exists = true };
        using var firstStorage = new GoogleDriveFileStorage(Settings(), client);
        Assert.False(File.Exists(stale));
        await using var active = await firstStorage.OpenReadAsync(Key, default);
        using var secondStorage = new GoogleDriveFileStorage(Settings(), new TransferClient());
        Assert.True(File.Exists(client.Output!.Name));
        Assert.True(File.Exists(unrelated));
        Assert.Equal(7, active.ReadByte());
    }

    private GoogleDriveSettings Settings(int metadata = 30, int transfer = 30, int slots = 4) => new()
    {
        ClientId = "id", ClientSecret = "secret", RefreshToken = "refresh", FolderId = "folder-id",
        TimeoutSeconds = metadata, TransferTimeoutSeconds = transfer, DownloadSpoolPath = spoolRoot,
        MaxConcurrentDownloads = slots
    };

    public void Dispose()
    {
        if (Directory.Exists(spoolRoot)) Directory.Delete(spoolRoot, true);
    }

    private sealed class TransferClient : IGoogleDriveClient
    {
        public bool Exists { get; init; }
        public bool FailDownload { get; init; }
        public bool Oversize { get; init; }
        public long DownloadBytes { get; init; } = 1;
        public TimeSpan TransferDelay { get; init; }
        public int TransferCalls { get; private set; }
        public FileStream? Output { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<string>> FindAsync(string folderId, string key, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(Exists ? ["file-id"] : []);

        public async Task UploadAsync(string folderId, string key, Stream content, CancellationToken ct)
        {
            TransferCalls++;
            await Task.Delay(TransferDelay, ct);
            await content.CopyToAsync(Stream.Null, ct);
        }

        public async Task DownloadAsync(string id, Stream output, CancellationToken ct)
        {
            TransferCalls++;
            Output = Assert.IsAssignableFrom<FileStream>(output);
            Started.TrySetResult();
            await Task.Delay(TransferDelay, ct);
            if (Oversize)
            {
                output.Position = 500L * 1024 * 1024;
                await output.WriteAsync(new byte[1], ct);
                return;
            }
            var buffer = new byte[81920];
            Array.Fill(buffer, (byte)7);
            for (long remaining = DownloadBytes; remaining > 0;)
            {
                var count = (int)Math.Min(remaining, buffer.Length);
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
                if (FailDownload) throw new IOException("Interrupted transfer");
                remaining -= count;
            }
        }

        public Task DeleteAsync(string id, CancellationToken ct) => Task.CompletedTask;
        public Task CheckFolderAsync(string folderId, CancellationToken ct) => Task.CompletedTask;
    }
}
