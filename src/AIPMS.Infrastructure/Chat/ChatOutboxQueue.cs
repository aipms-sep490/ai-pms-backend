using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Features.Chat;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Chat;

internal sealed class ChatWakeSignal : IChatWakeSignal, IDisposable
{
    private readonly SemaphoreSlim signal = new(0,1);
    public void Pulse() { try { signal.Release(); } catch(SemaphoreFullException) { } }
    public async Task WaitAsync(TimeSpan timeout,CancellationToken ct) => await signal.WaitAsync(timeout,ct);
    public void Dispose() => signal.Dispose();
}

internal sealed class ChatOutboxQueue(AipmsDbContext db,TimeProvider clock) : IChatOutbox
{
    public async Task<ChatDispatch?> ClaimAsync(CancellationToken ct)
    {
        var now=clock.GetUtcNow().UtcDateTime;
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        var row=await db.Set<ChatOutbox>().FromSqlInterpolated($"""
            SELECT TOP(1) * FROM dbo.chat_outbox WITH(UPDLOCK,READPAST,ROWLOCK)
            WHERE (status='PENDING' AND next_attempt_at<={now}) OR (status='PROCESSING' AND lease_until<{now})
            ORDER BY id
            """).SingleOrDefaultAsync(ct);
        if(row is null) return null;
        await db.Entry(row).ReloadAsync(ct);
        row.Status="PROCESSING";row.AttemptCount++;row.LeaseToken=Guid.NewGuid();row.LeaseUntil=now.AddMinutes(2);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return new(row.Id,row.LeaseToken.Value,new(row.EventId,row.ConversationId.ToString(System.Globalization.CultureInfo.InvariantCulture),row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)),row.EventType);
    }
    public async Task CompleteAsync(ChatDispatch d,bool success,CancellationToken ct)
    {
        var now=clock.GetUtcNow().UtcDateTime;
        await db.Set<ChatOutbox>().Where(o=>o.Id==d.Id && o.LeaseToken==d.Lease && o.Status=="PROCESSING")
            .ExecuteUpdateAsync(s=>s.SetProperty(o=>o.Status,o=>success?"SUCCEEDED":o.AttemptCount>=10?"FAILED":"PENDING")
                .SetProperty(o=>o.CompletedAt,success?(DateTime?)now:null)
                .SetProperty(o=>o.NextAttemptAt,now.AddSeconds(30))
                .SetProperty(o=>o.ErrorCode,success?null:"CHAT_DISPATCH_FAILED")
                .SetProperty(o=>o.LeaseUntil,(DateTime?)null),ct);
    }
}
