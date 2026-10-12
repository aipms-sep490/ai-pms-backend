using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Features.Chat.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Chat;

internal sealed class ChatUploadCleanup(AipmsDbContext db, IFileStorage storage, TimeProvider clock) : IChatUploadCleanup
{
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var count = 0;
        for (var i = 0; i < 20; i++)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            // Commit ineligibility before deleting bytes; a failed cleanup cannot reopen an upload for send.
            var row = await db.Set<ChatUpload>().FromSqlInterpolated($"""
                SELECT TOP(1) * FROM dbo.chat_uploads WITH(UPDLOCK,READPAST,ROWLOCK)
                WHERE status IN ('UPLOADING','READY','DELETING') AND expires_at<={now} ORDER BY expires_at,id
                """).SingleOrDefaultAsync(ct);
            if (row is null) break;
            await db.Entry(row).ReloadAsync(ct);
            if (await db.Set<ChatMessageAttachment>().AnyAsync(a => a.UploadId == row.Id, ct))
                throw new InvalidOperationException("Linked upload must never be cleaned as an orphan.");
            row.Status = "DELETING"; await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            try { await storage.DeleteAsync(row.StorageKey, ct); }
            catch
            {
                // Back off this object so a broken provider object cannot starve other cleanup work.
                await db.Set<ChatUpload>().Where(u => u.Id == row.Id && u.Status == "DELETING")
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiresAt, now.AddMinutes(5)), CancellationToken.None);
                throw;
            }
            await db.Set<ChatUpload>().Where(u => u.Id == row.Id && u.Status == "DELETING")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "DELETED"), ct);
            count++;
        }
        return count;
    }
}

internal sealed class ChatNotificationPolicy(AipmsDbContext db, TimeProvider clock) : IChatNotificationPolicy
{
    public async Task<IReadOnlySet<long>> NotifyAsync(long conversation, long messageId, CancellationToken ct) =>
        (await (from m in db.Set<ChatMessage>() from i in db.Set<ChatMembershipInterval>()
            where m.Id == messageId && m.ConversationId == conversation && m.RecalledAt == null
                && i.ConversationId == conversation && i.UserId != m.SenderId && i.LeftAt == null && i.FromSequence <= m.Sequence
                && !db.Set<ChatMemberState>().Any(s => s.ConversationId == conversation && s.UserId == i.UserId
                    && (s.MutedUntil > clock.GetUtcNow().UtcDateTime || s.LastReadSequence >= m.Sequence))
            select i.UserId).ToArrayAsync(ct)).ToHashSet();
}
