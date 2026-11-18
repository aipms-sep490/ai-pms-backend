using System.Net;
using System.Security.Cryptography;
using AIPMS.Infrastructure.Storage;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class GoogleDriveStreamingTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private readonly string root = Path.Combine(Path.GetTempPath(), "aipms-stream-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task First_bytes_arrive_before_the_remote_transfer_finishes_without_disk_spooling()
    {
        var client = new StreamingClient { Bytes = 128 * 1024, PauseAfterFirst = true };
        using var storage = Storage(client);
        await using var stream = await storage.OpenSequentialReadAsync(Key, default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stream.CanSeek);
        Assert.False(client.Finished.Task.IsCompleted);
        var bytes = new byte[1024];
        Assert.Equal(bytes.Length, await stream.ReadAsync(bytes));
        Assert.All(bytes, value => Assert.Equal(7, value));
        Assert.Empty(Directory.EnumerateFiles(root));
        client.Continue.TrySetResult();
        await stream.CopyToAsync(Stream.Null);
        await client.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Slow_consumer_applies_bounded_backpressure_and_disposal_stops_remote_work()
    {
        var client = new StreamingClient { Bytes = 500L * 1024 * 1024 };
        using var storage = Storage(client);
        var stream = await storage.OpenSequentialReadAsync(Key, default);
        await Task.Delay(100);
        Assert.InRange(client.BytesWritten, 64 * 1024, 256 * 1024);
        Assert.False(client.Finished.Task.IsCompleted);
        await stream.DisposeAsync();
        await client.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(client.Cancelled);
        Assert.Empty(Directory.EnumerateFiles(root));
    }

    [Fact]
    public async Task Full_500_MiB_stream_preserves_bytes_and_checksum_without_full_file_buffer()
    {
        const long size = 500L * 1024 * 1024;
        var client = new StreamingClient { Bytes = size };
        using var storage = Storage(client);
        await using var stream = await storage.OpenSequentialReadAsync(Key, default);
        using var actual = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var known = new byte[81920];
        Array.Fill(known, (byte)7);
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            actual.AppendData(buffer.AsSpan(0, read));
            expected.AppendData(known.AsSpan(0, read));
            total += read;
        }
        Assert.Equal(size, total);
        Assert.Equal(expected.GetHashAndReset(), actual.GetHashAndReset());
        Assert.Empty(Directory.EnumerateFiles(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_errors_are_sanitized_before_or_after_first_bytes(bool afterFirst)
    {
        var client = new StreamingClient { Fail = true, PauseAfterFirst = afterFirst, Bytes = 128 * 1024 };
        using var storage = Storage(client);
        IOException error;
        if (afterFirst)
        {
            await using var stream = await storage.OpenSequentialReadAsync(Key, default);
            var buffer = new byte[1024];
            Assert.True(await stream.ReadAsync(buffer) > 0);
            client.Continue.TrySetResult();
            error = await Assert.ThrowsAsync<IOException>(() => stream.CopyToAsync(Stream.Null));
        }
        else error = await Assert.ThrowsAsync<IOException>(() => storage.OpenSequentialReadAsync(Key, default));
        Assert.DoesNotContain("secret-provider-response", error.ToString());
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task Cancellation_before_first_byte_preserves_cancellation_and_releases_slot()
    {
        var client = new StreamingClient { PauseBeforeFirst = true };
        using var storage = Storage(client, slots: 1);
        using var cancel = new CancellationTokenSource();
        var opening = storage.OpenSequentialReadAsync(Key, cancel.Token);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        client.PauseBeforeFirst = false;
        await using var second = await storage.OpenSequentialReadAsync(Key, default);
        Assert.Equal(7, second.ReadByte());
    }

    [Fact]
    public async Task Disposing_twice_releases_exactly_one_slot_and_allows_next_download()
    {
        var client = new StreamingClient();
        using var storage = Storage(client, slots: 1, timeout: 1);
        var stream = await storage.OpenSequentialReadAsync(Key, default);
        await Assert.ThrowsAsync<IOException>(() => storage.OpenSequentialReadAsync(Key, default));
        stream.Dispose();
        await stream.DisposeAsync();
        await using var next = await storage.OpenSequentialReadAsync(Key, default);
        Assert.Equal(7, next.ReadByte());
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task Transfer_deadline_interrupts_read_after_initial_bytes()
    {
        var client = new StreamingClient { Bytes = 128 * 1024, PauseAfterFirst = true };
        using var storage = Storage(client, timeout: 1);
        await using var stream = await storage.OpenSequentialReadAsync(Key, default);
        var error = await Assert.ThrowsAsync<IOException>(() => stream.CopyToAsync(Stream.Null));
        Assert.Contains("timed out", error.Message);
    }

    private GoogleDriveFileStorage Storage(StreamingClient client, int slots = 4, int timeout = 30) => new(new GoogleDriveSettings
    {
        ClientId = "id", ClientSecret = "secret", RefreshToken = "refresh", FolderId = "folder",
        TimeoutSeconds = timeout, TransferTimeoutSeconds = timeout, MaxConcurrentDownloads = slots, DownloadSpoolPath = root
    }, client);

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private sealed class StreamingClient : IGoogleDriveClient
    {
        public long Bytes { get; init; } = 1;
        public bool PauseBeforeFirst { get; set; }
        public bool PauseAfterFirst { get; init; }
        public bool Fail { get; init; }
        public long BytesWritten { get; private set; }
        public bool Cancelled { get; private set; }
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<string>> FindAsync(string folderId, string key, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(["id"]);
        public async Task DownloadAsync(string id, Stream output, CancellationToken ct)
        {
            Calls++;
            Started.TrySetResult();
            try
            {
                if (PauseBeforeFirst) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                if (Fail && !PauseAfterFirst) throw ProviderError();
                var buffer = new byte[64 * 1024];
                Array.Fill(buffer, (byte)7);
                for (long left = Bytes; left > 0;)
                {
                    var count = (int)Math.Min(buffer.Length, left);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    BytesWritten += count;
                    left -= count;
                    if (PauseAfterFirst)
                    {
                        await Continue.Task.WaitAsync(ct);
                        if (Fail) throw ProviderError();
                    }
                }
            }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            finally { Finished.TrySetResult(); }
        }
        private static Exception ProviderError() => new Google.GoogleApiException("drive", "secret-provider-response")
            { HttpStatusCode = HttpStatusCode.Forbidden };
        public Task UploadAsync(string folderId, string key, Stream content, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct) => Task.CompletedTask;
        public Task CheckFolderAsync(string folderId, CancellationToken ct) => Task.CompletedTask;
    }
}
