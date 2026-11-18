using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Files;
using AIPMS.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class ProjectFileUploadSessionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aipms-upload-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock clock = new();
    private ProjectFileUploadSessionStore Store(long? quota = null) => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["FileStorage:UploadSessionsPath"] = root,
            ["FileStorage:UploadSessionQuotaBytes"] = quota?.ToString() }).Build(), clock);
    private static CreateProjectFileUploadSession Request(long size = 8) => new("TASK", 1, "evidence.pdf", "application/pdf", size);
    private static ProjectFileDto Result(ProjectFileUploadSessionStore.Entry entry) => new(42, entry.ParentType,
        entry.ParentId, entry.FileName, entry.ContentType, entry.TotalBytes, "checksum", entry.ActorId, DateTime.UtcNow);
    private static Task<ProjectFileUploadSession> Append(ProjectFileUploadSessionStore store, string id, long offset, byte[] bytes, long actor = 10)
        => store.AppendAsync(actor, id, offset, new MemoryStream(bytes), bytes.Length, default);

    [Fact]
    public async Task Restart_resumes_at_durable_offset_and_matching_duplicate_is_idempotent()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(), default);
        await Append(store, session.SessionId, 0, [1, 2, 3, 4]);
        var restarted = Store();
        Assert.Equal(4, (await restarted.GetAsync(10, session.SessionId, default)).NextOffset);
        Assert.Equal(4, (await Append(restarted, session.SessionId, 0, [1, 2, 3, 4])).NextOffset);
        await Assert.ThrowsAsync<ConflictException>(() => Append(restarted, session.SessionId, 0, [1, 2, 0, 4]));
        Assert.Equal(8, (await Append(restarted, session.SessionId, 4, [5, 6, 7, 8])).NextOffset);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, await File.ReadAllBytesAsync(Path.Combine(root, session.SessionId + ".part")));
    }

    [Fact]
    public async Task Owner_is_required_for_read_append_complete_and_cancel()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(), default);
        await Assert.ThrowsAsync<NotFoundException>(() => store.GetAsync(20, session.SessionId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => Append(store, session.SessionId, 0, [1], 20));
        await Assert.ThrowsAsync<NotFoundException>(() => store.CompleteAsync(20, session.SessionId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => store.CancelAsync(20, session.SessionId, default));
    }

    [Fact]
    public async Task Rejects_oversize_files_chunks_wrong_offsets_and_incomplete_completion()
    {
        var store = Store();
        await Assert.ThrowsAsync<PayloadTooLargeException>(() => store.CreateAsync(10, Request(UploadPolicies.ProjectFileLimit + 1), default));
        await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(10, Request() with { FileName = " evidence.pdf" }, default));
        var session = await store.CreateAsync(10, Request(), default);
        await Assert.ThrowsAsync<PayloadTooLargeException>(() => store.AppendAsync(10, session.SessionId, 0, Stream.Null, UploadPolicies.ProjectFileChunkSize + 1, default));
        await Assert.ThrowsAsync<ConflictException>(() => Append(store, session.SessionId, 1, [1]));
        await Assert.ThrowsAsync<ConflictException>(() => Append(store, session.SessionId, 0, new byte[9]));
        await Assert.ThrowsAsync<ConflictException>(() => store.CompleteAsync(10, session.SessionId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => store.GetAsync(10, "../../test", default));
    }

    [Fact]
    public async Task Partial_request_is_rolled_back_and_crash_tail_is_truncated_before_retry()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(), default);
        await Assert.ThrowsAsync<ValidationException>(() => store.AppendAsync(10, session.SessionId, 0, new MemoryStream([1, 2]), 4, default));
        Assert.Equal(0, new FileInfo(Path.Combine(root, session.SessionId + ".part")).Length);
        await Append(store, session.SessionId, 0, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(Path.Combine(root, session.SessionId + ".part"), [1, 2, 3, 4, 99, 99]);
        await Append(Store(), session.SessionId, 4, [5, 6, 7, 8]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, await File.ReadAllBytesAsync(Path.Combine(root, session.SessionId + ".part")));
    }

    [Fact]
    public async Task Per_user_and_global_reservations_are_enforced_and_cancel_releases_quota()
    {
        var store = Store(UploadPolicies.ProjectFileLimit);
        var first = await store.CreateAsync(10, Request(UploadPolicies.ProjectFileLimit), default);
        await Assert.ThrowsAsync<ConflictException>(() => store.CreateAsync(20, Request(), default));
        await store.CancelAsync(10, first.SessionId, default);
        await store.CreateAsync(10, Request(), default);
        await store.CreateAsync(10, Request(), default);
        await Assert.ThrowsAsync<ConflictException>(() => store.CreateAsync(10, Request(), default));
        await store.CreateAsync(20, Request(), default);
    }

    [Fact]
    public async Task Durable_processing_survives_restart_and_completed_ack_is_idempotent()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(4), default);
        await Append(store, session.SessionId, 0, [1, 2, 3, 4]);
        Assert.Equal("PROCESSING", (await store.CompleteAsync(10, session.SessionId, default)).State);
        var restarted = Store();
        var calls = 0;
        await restarted.ProcessAsync(session.SessionId, (entry, stream, ct) =>
        {
            calls++;
            Assert.Equal(10, entry.ActorId);
            Assert.Equal(4, stream.Length);
            return Task.FromResult(Result(entry));
        }, default);
        await restarted.ProcessAsync(session.SessionId, (_, _, _) => throw new InvalidOperationException(), default);
        var completed = await restarted.CompleteAsync(10, session.SessionId, default);
        Assert.Equal("COMPLETED", completed.State);
        Assert.Equal(42, completed.File!.Id);
        Assert.Equal(1, calls);
        Assert.False(File.Exists(Path.Combine(root, session.SessionId + ".part")));
    }

    [Fact]
    public async Task Polling_remains_available_during_processing_and_cancel_is_blocked()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(1), default);
        await Append(store, session.SessionId, 0, [1]);
        await store.CompleteAsync(10, session.SessionId, default);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processing = store.ProcessAsync(session.SessionId, async (entry, _, _) =>
        {
            started.SetResult();
            await release.Task;
            return Result(entry);
        }, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal("PROCESSING", (await store.GetAsync(10, session.SessionId, default).WaitAsync(TimeSpan.FromSeconds(2))).State);
            await Assert.ThrowsAsync<ConflictException>(() => store.CancelAsync(10, session.SessionId, default));
        }
        finally { release.SetResult(); await processing; }
    }

    [Fact]
    public async Task Failure_keeps_data_for_retry_without_leaking_provider_details()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(1), default);
        await Append(store, session.SessionId, 0, [1]);
        await store.CompleteAsync(10, session.SessionId, default);
        await store.ProcessAsync(session.SessionId, (_, _, _) => throw new IOException("private-storage-token"), default);
        var failed = await store.GetAsync(10, session.SessionId, default);
        Assert.Equal("FAILED", failed.State);
        Assert.DoesNotContain("private-storage-token", failed.Error);
        Assert.True(File.Exists(Path.Combine(root, session.SessionId + ".part")));
        await store.CompleteAsync(10, session.SessionId, default);
        await store.ProcessAsync(session.SessionId, (entry, _, _) => Task.FromResult(Result(entry)), default);
        Assert.Equal("COMPLETED", (await store.GetAsync(10, session.SessionId, default)).State);
    }

    [Fact]
    public async Task Shutdown_keeps_processing_for_replay_and_expired_data_is_cleaned()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(1), default);
        await Append(store, session.SessionId, 0, [1]);
        await store.CompleteAsync(10, session.SessionId, default);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ProcessAsync(session.SessionId, (_, _, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }, cancellation.Token));
        Assert.Equal("PROCESSING", (await Store().GetAsync(10, session.SessionId, default)).State);
        clock.Now = clock.Now.AddHours(25);
        await Assert.ThrowsAsync<NotFoundException>(() => store.GetAsync(10, session.SessionId, default));
        await store.CleanupAsync(default);
        Assert.Empty(Directory.EnumerateFiles(root, "*.part"));
        Assert.Empty(Directory.EnumerateFiles(root, "*.json"));
    }

    [Fact]
    public async Task Cancel_defers_storage_reconciliation_and_retains_metadata_when_reconciliation_fails()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(1), default);
        await Append(store, session.SessionId, 0, [1]);
        await store.CancelAsync(10, session.SessionId, default);
        await Assert.ThrowsAsync<NotFoundException>(() => store.GetAsync(10, session.SessionId, default));
        Assert.False(File.Exists(Path.Combine(root, session.SessionId + ".part")));
        await Assert.ThrowsAsync<IOException>(() => store.CleanupAsync(default, (_, _) => throw new IOException("Database unavailable")));
        Assert.True(File.Exists(Path.Combine(root, session.SessionId + ".json")));
        var reconciled = 0;
        await store.CleanupAsync(default, (entry, _) =>
        {
            Assert.Equal(session.SessionId, entry.SessionId);
            reconciled++;
            return Task.CompletedTask;
        });
        Assert.Equal(1, reconciled);
        Assert.False(File.Exists(Path.Combine(root, session.SessionId + ".json")));
    }

    [Fact]
    public async Task Corrupt_session_is_quarantined_without_blocking_other_uploads_or_deleting_its_content()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(1), default);
        await File.WriteAllTextAsync(Path.Combine(root, session.SessionId + ".json"), "{broken");
        var another = await store.CreateAsync(20, Request(1), default);
        Assert.NotNull(await store.GetAsync(20, another.SessionId, default));
        Assert.True(File.Exists(Path.Combine(root, session.SessionId + ".quarantined")));
        Assert.True(File.Exists(Path.Combine(root, session.SessionId + ".part")));
    }

    [Fact]
    public async Task Expired_completed_session_cleanup_never_reconciles_its_committed_storage()
    {
        var store = Store();
        var session = await store.CreateAsync(10, Request(1), default);
        await Append(store, session.SessionId, 0, [1]);
        await store.CompleteAsync(10, session.SessionId, default);
        await store.ProcessAsync(session.SessionId, (entry, _, _) => Task.FromResult(Result(entry)), default);
        clock.Now = clock.Now.AddHours(25);
        await store.CleanupAsync(default, (_, _) => throw new InvalidOperationException("Committed objects must be retained"));
        Assert.Empty(Directory.EnumerateFiles(root, "*.json"));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
