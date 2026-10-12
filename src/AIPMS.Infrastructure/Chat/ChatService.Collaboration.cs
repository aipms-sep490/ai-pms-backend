using System.IO;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Chat;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Chat;

internal sealed partial class ChatService
{
    private static ChatAttachmentDto Attachment(ChatUpload u) => new(S(u.Id), u.FileName, u.ContentType, u.SizeBytes, u.Sha256);
    // Fixed reactions keep the unique key, response size and provider-neutral contract bounded.
    private static readonly string[] Emojis = ["\U0001F44D", "\u2764\uFE0F", "\U0001F602", "\U0001F62E", "\U0001F622", "\U0001F64F"];

    public async Task<ChatAttachmentDto> UploadAsync(long actor, long id, UploadContent upload, CancellationToken ct)
    {
        await Enabled(actor, ct);
        if (!await access.CanAccessAsync(actor, id, ct)) throw Hidden();
        if (upload.Length is <= 0 or > 10 * 1024 * 1024) throw new ArgumentException("CHAT_FILE_SIZE_LIMIT");
        var validated = await UploadValidator.ReadAsync(upload, ct);
        var store = storage ?? throw new InvalidOperationException("Chat storage is not configured.");
        // Commit a reservation first. Interrupted uploads remain discoverable by durable cleanup.
        var row = await InConversation(actor, id, true, async _ => {
            if (await db.Set<ChatUpload>().CountAsync(u => u.ConversationId == id && u.UploadedBy == actor
                && (u.Status == "READY" || u.Status == "UPLOADING"), ct) >= 20)
                throw new ConflictException("At most 20 unlinked uploads per conversation and sender.", "CHAT_UPLOAD_LIMIT");
            var u = new ChatUpload { ConversationId = id, UploadedBy = actor, StorageKey = Guid.NewGuid().ToString("N"),
                FileName = validated.FileName, ContentType = validated.ContentType, SizeBytes = validated.Bytes.Length,
                Sha256 = validated.Sha256, CreatedAt = Now, ExpiresAt = Now.AddHours(24) };
            db.Add(u); await db.SaveChangesAsync(ct); return u;
        }, ct);
        var createFailed = false;
        try { return await InConversation(actor, id, true, async _ => {
            var u = await db.Set<ChatUpload>().FromSqlInterpolated($"SELECT * FROM dbo.chat_uploads WITH(UPDLOCK,HOLDLOCK) WHERE id={row.Id}").SingleAsync(ct);
            await db.Entry(u).ReloadAsync(ct);
            if (u.Status != "UPLOADING" || u.ExpiresAt <= Now) throw new ConflictException("Upload expired.", "CHAT_ATTACHMENT_UNAVAILABLE");
            using var content = new MemoryStream(validated.Bytes, writable: false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await store.WriteAsync(u.StorageKey, content, timeout.Token); }
            catch { createFailed = true; throw; }
            u.Status = "READY";
            db.AuditLogs.Add(new() { ActorUserId = actor, Action = "CHAT_FILE_UPLOADED", EntityType = "CHAT_UPLOAD", EntityId = S(u.Id), Outcome = "SUCCESS", OccurredAt = Now });
            return Attachment(u);
        }, ct); }
        catch
        {
            // A failed create owns no object (including collisions); never delete that key.
            // Successful writes followed by uncertain commit stay reserved for reconciliation.
            if (createFailed)
                await db.Set<ChatUpload>().Where(u => u.Id == row.Id && u.Status == "UPLOADING")
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "DELETED"), CancellationToken.None);
            throw;
        }
    }

    public async Task<FileDownload> DownloadAsync(long actor, long id, long messageId, long attachmentId, CancellationToken ct)
    {
        Stream? stream = null;
        try
        {
            return await InConversation(actor, id, false, async _ => {
                if (!await Visible(actor, id).AnyAsync(m => m.Id == messageId && m.RecalledAt == null, ct)) throw Hidden();
                var upload = await (from a in db.Set<ChatMessageAttachment>() join u in db.Set<ChatUpload>() on a.UploadId equals u.Id
                    where a.MessageId == messageId && a.UploadId == attachmentId && u.ConversationId == id && u.Status == "ATTACHED" select u).SingleOrDefaultAsync(ct) ?? throw Hidden();
                try { stream = await storage!.OpenReadAsync(upload.StorageKey, ct); }
                catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw Hidden(); }
                return new FileDownload(stream, upload.ContentType, upload.FileName);
            }, ct);
        }
        catch { if (stream is not null) await stream.DisposeAsync(); throw; }
    }

    public Task<ChatMessageDto> ReactAsync(long actor, long id, long messageId, string emoji, bool add, CancellationToken ct) =>
        InConversation(actor, id, true, async c => {
            if (!Emojis.Contains(emoji)) throw new ArgumentException("CHAT_INVALID_REACTION");
            var message = await Visible(actor, id).SingleOrDefaultAsync(m => m.Id == messageId && m.RecalledAt == null, ct) ?? throw Hidden();
            var reaction = await db.Set<ChatReaction>().FindAsync([messageId, actor, emoji], ct);
            if (add && reaction is null || !add && reaction is not null)
            {
                if (add) db.Add(new ChatReaction { MessageId = messageId, UserId = actor, Emoji = emoji, CreatedAt = Now });
                else db.Remove(reaction!);
                Event(c, actor, "ReactionsChanged", add ? "CHAT_REACTION_ADDED" : "CHAT_REACTION_REMOVED");
                await db.SaveChangesAsync(ct);
            }
            return (await Map(actor, [message], ct)).Single();
        }, ct);

    public Task<ChatConversationDto> MuteAsync(long actor, long id, ChatMuteRequest request, CancellationToken ct) =>
        InConversation(actor, id, false, async c => {
            var until = request.MutedUntil?.UtcDateTime;
            if (until.HasValue && (until <= Now || until > Now.AddDays(365))) throw new ArgumentException("CHAT_INVALID_MUTE_UNTIL");
            var state = await db.Set<ChatMemberState>().FindAsync([id, actor], ct);
            if (state is null) { state = new() { ConversationId = id, UserId = actor }; db.Add(state); }
            if (state.MutedUntil != until)
            {
                state.MutedUntil = until; state.UpdatedAt = Now;
                Event(c, actor, "ReadStateChanged", "CHAT_MUTE_UPDATED");
                await db.SaveChangesAsync(ct);
            }
            return (await Summaries(actor, [c], ct)).Single();
        }, ct);
}
