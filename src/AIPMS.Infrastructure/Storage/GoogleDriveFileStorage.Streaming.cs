using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Storage;

internal sealed partial class GoogleDriveFileStorage
{
    public async Task<Stream> OpenSequentialReadAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        string? id = null;
        await ExecuteAsync(async token => id = await FindIdAsync(key, token)
            ?? throw new FileNotFoundException("Google Drive object was not found."), ct);
        await ExecuteAsync(token => downloadSlots.WaitAsync(token), ct);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 256 * 1024, resumeWriterThreshold: 128 * 1024,
            useSynchronizationContext: false));
        var firstData = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = ProduceAsync();
        try
        {
            // Surface missing/provider errors before HTTP headers while returning after the first bytes.
            await firstData.Task.WaitAsync(ct);
            return new SequentialDownloadStream(pipe.Reader.AsStream(), lifetime, producer, downloadSlots);
        }
        catch
        {
            lifetime.Cancel();
            await pipe.Reader.CompleteAsync();
            await producer;
            lifetime.Dispose();
            downloadSlots.Release();
            // Observe a provider fault that raced with caller cancellation.
            if (firstData.Task.IsFaulted) _ = firstData.Task.Exception;
            throw;
        }

        async Task ProduceAsync()
        {
            Exception? failure = null;
            try
            {
                using var output = new PipeDownloadWriter(pipe.Writer, firstData);
                await ExecuteAsync(token => client.DownloadAsync(id!, output, token), lifetime.Token,
                    settings.TransferTimeoutSeconds);
            }
            catch (Exception ex) { failure = ex; firstData.TrySetException(ex); }
            finally
            {
                firstData.TrySetResult();
                await pipe.Writer.CompleteAsync(failure);
            }
        }
    }

    private sealed class PipeDownloadWriter(PipeWriter writer, TaskCompletionSource firstData) : Stream
    {
        private const long MaxBytes = 500L * 1024 * 1024;
        private long written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => written;
        public override long Position { get => written; set => throw new NotSupportedException(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (buffer.Length > MaxBytes - written) throw new IOException("Google Drive file exceeds the 500 MiB download limit.");
            while (!buffer.IsEmpty)
            {
                ct.ThrowIfCancellationRequested();
                var count = Math.Min(buffer.Length, 64 * 1024);
                buffer[..count].CopyTo(writer.GetMemory(count));
                writer.Advance(count);
                written += count;
                firstData.TrySetResult();
                var result = await writer.FlushAsync(ct);
                if (result.IsCanceled) throw new OperationCanceledException(ct);
                if (result.IsCompleted) throw new IOException("The download response was closed.");
                buffer = buffer[count..];
            }
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override void Write(byte[] buffer, int offset, int count)
            => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class SequentialDownloadStream(Stream input, CancellationTokenSource lifetime,
        Task producer, SemaphoreSlim slots) : Stream
    {
        private int disposed;
        private Task? cleanup;
        public override bool CanRead => Volatile.Read(ref disposed) == 0;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => input.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => input.ReadAsync(buffer, ct);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => input.ReadAsync(buffer, offset, count, ct);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        private Task CloseAsync()
        {
            lock (this)
            {
                if (cleanup is not null) return cleanup;
                Interlocked.Exchange(ref disposed, 1);
                lifetime.Cancel();
                input.Dispose();
                return cleanup = FinishAsync();
            }
        }
        private async Task FinishAsync()
        {
            try { await producer; }
            finally { lifetime.Dispose(); slots.Release(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _ = CloseAsync();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await CloseAsync();
            GC.SuppressFinalize(this);
        }
    }
}
