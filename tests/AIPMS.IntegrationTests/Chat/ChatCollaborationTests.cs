using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Features.Chat;
using AIPMS.Infrastructure.Chat;
using AIPMS.IntegrationTests.FinalSubmissions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Chat;

public sealed class ChatCollaborationTests(FinalSubmissionDraftDatabaseFixture database) : IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response) { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private static string Url(string id) => $"/api/v1/chat/conversations/{id}";
    private static async Task<ChatAttachmentDto> Upload(HttpClient client, string id, string content = "private attachment")
    {
        using var form = new MultipartFormDataContent(); var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new("text/plain"); form.Add(file, "file", "document.txt");
        return await Body<ChatAttachmentDto>(await client.PostAsync(Url(id) + "/uploads", form));
    }
    private static async Task<ChatConversationDto> Group(HttpClient client, long project) => await Body<ChatConversationDto>(await client.PostAsync($"/api/v1/chat/projects/{project}/conversation", null));
    private static async Task<ChatMessageDto> Send(HttpClient client, string id, params string[] files) =>
        await Body<ChatMessageDto>(await client.PostAsJsonAsync(Url(id) + "/messages", new ChatSendRequest(Guid.NewGuid(), null, AttachmentIds: files)));

    [Fact]
    public async Task Attachment_only_multiple_files_retry_download_recall_and_readback()
    {
        var s = await database.Seed(); using var app = new Factory(database.ConnectionString); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId); var c = await Group(leader, s.ProjectId);
        var a = await Upload(leader, c.Id); var b = await Upload(leader, c.Id, "second");
        var request = new ChatSendRequest(Guid.NewGuid(), null, AttachmentIds: [a.Id, b.Id]);
        var message = await Body<ChatMessageDto>(await leader.PostAsJsonAsync(Url(c.Id) + "/messages", request));
        var retry = await Body<ChatMessageDto>(await leader.PostAsJsonAsync(Url(c.Id) + "/messages", request));
        Assert.Equal(message.Id, retry.Id); Assert.Equal(2, message.Attachments!.Count);
        var download = Url(c.Id) + $"/messages/{message.Id}/attachments/{a.Id}/download";
        var response = await member.GetAsync(download); response.EnsureSuccessStatusCode();
        Assert.Equal("private attachment", await response.Content.ReadAsStringAsync()); Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition!.ToString());
        using var outsider = app.CreateAuthenticatedClient(s.Users.Admin); Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(download)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(c.Id) + "/messages", request with { AttachmentIds = [a.Id] })).StatusCode);
        var recalled = await Body<ChatMessageDto>(await leader.PostAsJsonAsync(Url(c.Id) + $"/messages/{message.Id}/recall", new ChatRecallRequest(message.ConcurrencyToken)));
        Assert.Empty(recalled.Attachments!); Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(download)).StatusCode);
        await using var db = database.CreateContext(); var mid = long.Parse(message.Id);
        Assert.Equal(2, await db.Set<M.ChatMessageAttachment>().CountAsync(a => a.MessageId == mid));
        Assert.Single(await db.Set<M.ChatOutbox>().Where(o => o.MessageId == mid).ToArrayAsync());
        Assert.Single(await db.AuditLogs.Where(a => a.EntityId == c.Id && a.Action == "CHAT_SENT").ToArrayAsync());
    }

    [Fact]
    public async Task Upload_ownership_conversation_expiry_membership_and_mime_fail_closed()
    {
        var s = await database.Seed(); using var app = new Factory(database.ConnectionString); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId); var c = await Group(leader, s.ProjectId); var upload = await Upload(leader, c.Id);
        var send = new ChatSendRequest(Guid.NewGuid(), null, AttachmentIds: [upload.Id]);
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync(Url(c.Id) + "/messages", send)).StatusCode);
        var other = await Body<ChatConversationDto>(await leader.PostAsync($"/api/v1/chat/teams/{s.TeamId}/conversation", null));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(other.Id) + "/messages", send)).StatusCode);
        using (var form = new MultipartFormDataContent()) { var file = new ByteArrayContent("not pdf"u8.ToArray()); file.Headers.ContentType = new("application/pdf"); form.Add(file, "file", "fake.pdf");
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsync(Url(c.Id) + "/uploads", form)).StatusCode); }
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync(Url(c.Id) + "/messages", new ChatSendRequest(Guid.NewGuid(), null, AttachmentIds: Enumerable.Range(1,11).Select(x => x.ToString()).ToArray()))).StatusCode);
        var message = await Send(leader, c.Id, upload.Id);
        var download = Url(c.Id) + $"/messages/{message.Id}/attachments/{upload.Id}/download";
        await using (var db = database.CreateContext()) { (await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.MemberId)).LeftAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(download)).StatusCode);
        await using (var db = database.CreateContext()) { (await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.MemberId)).LeftAt = null; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(download)).StatusCode);
        var expired = await Upload(leader, c.Id);
        await using (var db = database.CreateContext()) { (await db.Set<M.ChatUpload>().FindAsync(long.Parse(expired.Id)))!.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(c.Id) + "/messages", send with { ClientMessageId = Guid.NewGuid(), AttachmentIds = [expired.Id] })).StatusCode);
    }

    [Fact]
    public async Task Reactions_are_idempotent_concurrent_scoped_and_recall_hides_them()
    {
        var s = await database.Seed(); using var app = new Factory(database.ConnectionString); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId); var c = await Group(leader, s.ProjectId);
        var message = await Body<ChatMessageDto>(await leader.PostAsJsonAsync(Url(c.Id) + "/messages", new ChatSendRequest(Guid.NewGuid(), "React")));
        var url = Url(c.Id) + $"/messages/{message.Id}/reactions"; const string emoji = "\U0001F44D";
        var responses = await Task.WhenAll(member.PutAsJsonAsync(url, new ChatReactionRequest(emoji)), member.PutAsJsonAsync(url, new ChatReactionRequest(emoji)));
        foreach (var response in responses) Assert.Single((await Body<ChatMessageDto>(response)).Reactions!);
        var both = await Body<ChatMessageDto>(await leader.PutAsJsonAsync(url, new ChatReactionRequest(emoji))); Assert.Equal(2, both.Reactions!.Single().Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PutAsJsonAsync(url, new ChatReactionRequest("invalid"))).StatusCode);
        using var outsider = app.CreateAuthenticatedClient(s.Users.Admin); Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutAsJsonAsync(url, new ChatReactionRequest(emoji))).StatusCode);
        await Body<ChatMessageDto>(await member.DeleteAsync(url + "?emoji=" + Uri.EscapeDataString(emoji)));
        var removed = await Body<ChatMessageDto>(await member.DeleteAsync(url + "?emoji=" + Uri.EscapeDataString(emoji))); Assert.False(removed.Reactions!.Single().ReactedByMe);
        var recalled = await Body<ChatMessageDto>(await leader.PostAsJsonAsync(Url(c.Id) + $"/messages/{message.Id}/recall", new ChatRecallRequest(message.ConcurrencyToken)));
        Assert.Empty(recalled.Reactions!); Assert.Equal(HttpStatusCode.NotFound, (await member.PutAsJsonAsync(url, new ChatReactionRequest(emoji))).StatusCode);
        await using var db = database.CreateContext(); Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.EntityId == c.Id && a.Action.StartsWith("CHAT_REACTION_")));
    }

    [Fact]
    public async Task Mute_preserves_unread_and_read_access_and_notification_policy_rechecks_state()
    {
        var s = await database.Seed(); using var app = new Factory(database.ConnectionString); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId); var c = await Group(leader, s.ProjectId);
        var muted = await Body<ChatConversationDto>(await member.PutAsJsonAsync(Url(c.Id) + "/mute", new ChatMuteRequest(DateTimeOffset.UtcNow.AddHours(1)))); Assert.NotNull(muted.MutedUntil);
        var message = await Body<ChatMessageDto>(await leader.PostAsJsonAsync(Url(c.Id) + "/messages", new ChatSendRequest(Guid.NewGuid(), "Still unread")));
        var detail = await Body<ChatConversationDto>(await member.GetAsync(Url(c.Id))); Assert.Equal(1, detail.UnreadCount);
        Assert.Single((await Body<ChatPage<ChatMessageDto>>(await member.GetAsync(Url(c.Id) + "/messages"))).Items);
        await using var db = database.CreateContext(); var policy = new ChatNotificationPolicy(db, TimeProvider.System);
        Assert.DoesNotContain(s.MemberId, await policy.NotifyAsync(long.Parse(c.Id), long.Parse(message.Id), default));
        await Body<ChatConversationDto>(await member.PutAsJsonAsync(Url(c.Id) + "/mute", new ChatMuteRequest(null)));
        Assert.Contains(s.MemberId, await policy.NotifyAsync(long.Parse(c.Id), long.Parse(message.Id), default));
        Assert.DoesNotContain(s.Users.Student, await policy.NotifyAsync(long.Parse(c.Id), long.Parse(message.Id), default));
        (await member.PutAsJsonAsync(Url(c.Id) + "/read", new ChatReadRequest(message.Id))).EnsureSuccessStatusCode();
        Assert.DoesNotContain(s.MemberId, await policy.NotifyAsync(long.Parse(c.Id), long.Parse(message.Id), default));
    }

    [Fact]
    public async Task Orphan_cleanup_retries_failed_storage_delete_and_never_deletes_linked_uploads()
    {
        var s = await database.Seed(); using var app = new Factory(database.ConnectionString); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var c = await Group(leader, s.ProjectId); var orphan = await Upload(leader, c.Id); var linked = await Upload(leader, c.Id);
        await Send(leader, c.Id, linked.Id);
        await using (var db = database.CreateContext()) { await db.Set<M.ChatUpload>().Where(u => u.ConversationId == long.Parse(c.Id)).ExecuteUpdateAsync(u => u.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))); }
        app.Storage.FailDelete = true;
        await using (var db = database.CreateContext()) await Assert.ThrowsAsync<IOException>(() => new ChatUploadCleanup(db, app.Storage, TimeProvider.System).SweepAsync(default));
        app.Storage.FailDelete = false;
        await using (var db = database.CreateContext()) await db.Set<M.ChatUpload>().Where(u => u.Id == long.Parse(orphan.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        await using (var db = database.CreateContext()) Assert.True(await new ChatUploadCleanup(db, app.Storage, TimeProvider.System).SweepAsync(default) >= 1);
        await using (var db = database.CreateContext()) { Assert.Equal("DELETED", (await db.Set<M.ChatUpload>().FindAsync(long.Parse(orphan.Id)))!.Status);
            var row = (await db.Set<M.ChatUpload>().FindAsync(long.Parse(linked.Id)))!; Assert.Equal("ATTACHED", row.Status); Assert.True(app.Storage.Files.ContainsKey(row.StorageKey)); }
    }

    [Fact]
    public async Task Concurrent_attachment_send_and_audit_failure_preserve_atomic_link_and_retry()
    {
        var s = await database.Seed(); using var app = new Factory(database.ConnectionString); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var c = await Group(leader, s.ProjectId); var file = await Upload(leader, c.Id);
        var request = new ChatSendRequest(Guid.NewGuid(), null, AttachmentIds: [file.Id]);
        var options = new DbContextOptionsBuilder<AipmsDbContext>().UseSqlServer(database.ConnectionString).AddInterceptors(new RejectAudit()).Options;
        await using (var db = new AipmsDbContext(options))
        {
            var service = new ChatService(db, new ChatAccessService(db, TimeProvider.System), Options.Create(new ChatSettings { Enabled = true }), TimeProvider.System, new ChatWakeSignal(), storage: app.Storage);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendAsync(s.Users.Student, long.Parse(c.Id), request, default));
        }
        var responses = await Task.WhenAll(leader.PostAsJsonAsync(Url(c.Id) + "/messages", request), leader.PostAsJsonAsync(Url(c.Id) + "/messages", request));
        var first = await Body<ChatMessageDto>(responses[0]); var second = await Body<ChatMessageDto>(responses[1]);
        Assert.Equal(first.Id, second.Id); Assert.Equal("1", first.Sequence);
        await using var verify = database.CreateContext(); var id = long.Parse(file.Id);
        Assert.Single(await verify.Set<M.ChatMessageAttachment>().Where(a => a.UploadId == id).ToArrayAsync());
        Assert.Equal("ATTACHED", (await verify.Set<M.ChatUpload>().FindAsync(id))!.Status);
        var extra = await Upload(leader, c.Id); var extraId = long.Parse(extra.Id);
        var race = await Task.WhenAll(leader.PostAsJsonAsync(Url(c.Id) + "/messages", new ChatSendRequest(Guid.NewGuid(), null, AttachmentIds: [extra.Id])),
            leader.PostAsJsonAsync(Url(c.Id) + "/messages", new ChatSendRequest(Guid.NewGuid(), null, AttachmentIds: [extra.Id])));
        Assert.Single(race, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(race, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(await verify.Set<M.ChatMessageAttachment>().Where(a => a.UploadId == extraId).ToArrayAsync());
    }

    [Fact]
    public async Task Upload_size_missing_content_and_closed_conversation_are_enforced()
    {
        var s = await database.Seed(); using var app = new Factory(database.ConnectionString); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var c = await Group(leader, s.ProjectId);
        using (var form = new MultipartFormDataContent()) { var file = new ByteArrayContent(new byte[10 * 1024 * 1024 + 1]); file.Headers.ContentType = new("text/plain"); form.Add(file, "file", "too-big.txt");
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsync(Url(c.Id) + "/uploads", form)).StatusCode); }
        var upload = await Upload(leader, c.Id); var message = await Send(leader, c.Id, upload.Id); app.Storage.Files.Clear();
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(Url(c.Id) + $"/messages/{message.Id}/attachments/{upload.Id}/download")).StatusCode);
        await using (var db = database.CreateContext()) { (await db.Projects.FindAsync(s.ProjectId))!.Status = "COMPLETED"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Url(c.Id) + $"/messages/{message.Id}/reactions", new ChatReactionRequest("\U0001F44D"))).StatusCode);
    }

    [Fact]
    public async Task Additive_migration_rerun_preserves_attachment_only_message_reaction_and_mute()
    {
        var s = await database.Seed(); using var app = new Factory(database.ConnectionString); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var c = await Group(leader, s.ProjectId); var file = await Upload(leader, c.Id); var message = await Send(leader, c.Id, file.Id);
        await Body<ChatMessageDto>(await leader.PutAsJsonAsync(Url(c.Id) + $"/messages/{message.Id}/reactions", new ChatReactionRequest("\U0001F44D")));
        await Body<ChatConversationDto>(await leader.PutAsJsonAsync(Url(c.Id) + "/mute", new ChatMuteRequest(DateTimeOffset.UtcNow.AddHours(1))));
        await database.Migrate(); await database.Migrate();
        var rows = await Body<ChatPage<ChatMessageDto>>(await leader.GetAsync(Url(c.Id) + "/messages"));
        Assert.Equal(file.Id, rows.Items.Single().Attachments!.Single().Id); Assert.Single(rows.Items.Single().Reactions!);
        Assert.NotNull((await Body<ChatConversationDto>(await leader.GetAsync(Url(c.Id)))).MutedUntil);
    }

    [Theory]
    [InlineData("CHAT_REACTION_ADDED")]
    [InlineData("CHAT_MUTE_UPDATED")]
    public async Task Failed_audit_rolls_back_collaboration_mutation(string action)
    {
        var s = await database.Seed(); using var app = new Factory(database.ConnectionString); using var leader = app.CreateAuthenticatedClient(s.Users.Student);
        var c = await Group(leader, s.ProjectId); var message = await Body<ChatMessageDto>(await leader.PostAsJsonAsync(Url(c.Id) + "/messages", new ChatSendRequest(Guid.NewGuid(), "audit")));
        var options = new DbContextOptionsBuilder<AipmsDbContext>().UseSqlServer(database.ConnectionString).AddInterceptors(new RejectAudit(action)).Options;
        await using (var db = new AipmsDbContext(options))
        {
            var service = new ChatService(db, new ChatAccessService(db, TimeProvider.System), Options.Create(new ChatSettings { Enabled = true }), TimeProvider.System, new ChatWakeSignal());
            if (action == "CHAT_REACTION_ADDED") await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReactAsync(s.Users.Student, long.Parse(c.Id), long.Parse(message.Id), "\U0001F44D", true, default));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => service.MuteAsync(s.Users.Student, long.Parse(c.Id), new(DateTimeOffset.UtcNow.AddHours(1)), default));
        }
        await using var verify = database.CreateContext();
        Assert.False(await verify.Set<M.ChatReaction>().AnyAsync(r => r.MessageId == long.Parse(message.Id)));
        Assert.False(await verify.Set<M.ChatMemberState>().AnyAsync(r => r.ConversationId == long.Parse(c.Id) && r.MutedUntil != null));
        Assert.False(await verify.AuditLogs.AnyAsync(r => r.EntityId == c.Id && r.Action == action));
    }

    private sealed class RejectAudit(string action = "CHAT_SENT") : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<M.AuditLog>().Any(e => e.State == EntityState.Added && e.Entity.Action == action)) throw new InvalidOperationException("Injected audit failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Factory(string connection) : AipmsWebApplicationFactory
    {
        public MemoryStorage Storage { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] = connection, ["Chat:Enabled"] = "true", ["Chat:RealtimeEnabled"] = "false" }));
            builder.ConfigureServices(s => { s.RemoveAll<IFileStorage>(); s.AddSingleton<IFileStorage>(Storage);
                foreach (var descriptor in s.Where(d => d.ImplementationType?.Name == "ChatUploadCleanupWorker").ToArray()) s.Remove(descriptor); });
        }
    }
    private sealed class MemoryStorage : IFileStorage
    {
        public ConcurrentDictionary<string, byte[]> Files { get; } = new(); public bool FailDelete { get; set; }
        public async Task WriteAsync(string key, Stream content, CancellationToken ct) { using var bytes = new MemoryStream(); await content.CopyToAsync(bytes, ct); if (!Files.TryAdd(key, bytes.ToArray())) throw new IOException("Collision"); }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Files.TryGetValue(key, out var bytes) ? bytes : throw new FileNotFoundException()));
        public Task DeleteAsync(string key, CancellationToken ct) { if (FailDelete) throw new IOException("Transient delete failure"); Files.TryRemove(key, out _); return Task.CompletedTask; }
    }
}
