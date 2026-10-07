using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Chat;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Chat;

internal sealed class ChatService(AipmsDbContext db, ChatAccessService access, IOptions<ChatSettings> settings,
    TimeProvider clock, IChatWakeSignal wake, IChatCredentialGuard? credentials = null) : IChatService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string S(long n) => n.ToString(CultureInfo.InvariantCulture);
    internal static long Id(string s) => long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
        ? n : throw new ArgumentException("CHAT_INVALID_ID");
    private static int Size(int n) => n is >= 1 and <= 100 ? n : throw new ArgumentException("CHAT_INVALID_PAGE_SIZE");
    private async Task Enabled(long actor, CancellationToken ct)
    {
        if (!settings.Value.Enabled) throw new ServiceUnavailableException("Chat is disabled.", "CHAT_DISABLED");
        if (credentials is not null) await credentials.CheckAsync(actor,ct);
        if (!await access.Active(actor, ct)) throw new ForbiddenException("Account cannot use chat.", "CHAT_ACCOUNT_INACTIVE");
    }
    private static NotFoundException Hidden() => new("Chat resource", "hidden");
    private IQueryable<ChatMessage> Visible(long actor, long id) => db.Set<ChatMessage>().Where(m => m.ConversationId == id
        && db.Set<ChatMembershipInterval>().Any(i => i.ConversationId == id && i.UserId == actor && i.LeftAt == null && i.FromSequence <= m.Sequence));

    private async Task Sync(ChatConversation c, CancellationToken ct)
    {
        var eligible = await access.Eligible(c, ct);
        var members = await db.Set<ChatMembershipInterval>().Where(m => m.ConversationId == c.Id && m.LeftAt == null).ToListAsync(ct);
        foreach (var m in members)
            if (!eligible.TryGetValue(m.UserId, out var key) || key != m.SourceKey)
            { m.LeftAt = Now; m.LeftSequence = c.Sequence; }
        await db.SaveChangesAsync(ct);
        foreach (var (user, key) in eligible)
            if (!members.Any(m => m.UserId == user && m.LeftAt == null))
                db.Set<ChatMembershipInterval>().Add(new() { ConversationId = c.Id, UserId = user, SourceKey = key, FromSequence = c.Sequence + 1, JoinedAt = Now });
        await db.SaveChangesAsync(ct);
    }

    private async Task<T> InConversation<T>(long actor, long id, bool write, Func<ChatConversation,Task<T>> action, CancellationToken ct)
    {
        await Enabled(actor, ct);
        var initial = await db.Set<ChatConversation>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw Hidden();
        var sourceTeam = initial.TeamId ?? (initial.ProjectId.HasValue
            ? await db.Projects.Where(p => p.Id == initial.ProjectId).Select(p => (long?)p.TeamId).SingleAsync(ct) : null);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (initial.Kind != "DIRECT")
            {
                var team = sourceTeam!.Value;
                await db.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH(UPDLOCK,HOLDLOCK) WHERE id={team}").AsNoTracking().SingleAsync(ct);
                if (initial.ProjectId.HasValue)
                    await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH(UPDLOCK,HOLDLOCK) WHERE id={initial.ProjectId}").AsNoTracking().SingleAsync(ct);
            }
            var c = await db.Set<ChatConversation>().FromSqlInterpolated($"SELECT * FROM dbo.chat_conversations WITH(UPDLOCK,HOLDLOCK) WHERE id={id}").SingleAsync(ct);
            await db.Entry(c).ReloadAsync(ct);
            if (credentials is not null) await credentials.CheckAsync(actor,ct);
            if (!await access.CanAccessAsync(actor, id, ct)) throw Hidden();
            if (write && c.Status != "OPEN") throw new ConflictException("Conversation is read-only.", "CHAT_READ_ONLY");
            await Sync(c, ct);
            var result = await action(c);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            wake.Pulse();
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Chat changed; reload and retry.", "CHAT_STALE"); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { throw new ConflictException("Chat is busy; retry.", "CHAT_CONCURRENT_WRITE"); }
    }

    public async Task<ChatPage<ChatPerson>> ContactsAsync(long actor, string? search, string? cursor, int size, CancellationToken ct)
    {
        await Enabled(actor, ct); Size(size);
        if (search?.Length > 100) throw new ArgumentException("CHAT_SEARCH_TOO_LONG");
        var after = cursor is null ? 0 : Id(cursor);
        var people = await db.Users.AsNoTracking().Where(u => u.Id > after && access.Contacts(actor).Contains(u.Id)
            && (search == null || u.FullName.Contains(search))).OrderBy(u => u.Id).Take(size + 1)
            .Select(u => new { u.Id, u.FullName }).ToListAsync(ct);
        return new(people.Take(size).Select(p => new ChatPerson(S(p.Id), p.FullName)).ToList(),
            people.Count > size ? S(people[size-1].Id) : null, people.Count > size);
    }

    public async Task<ChatConversationDto> OpenAsync(long actor, string kind, long target, CancellationToken ct)
    {
        await Enabled(actor, ct);
        if (target <= 0 || kind is not ("DIRECT" or "TEAM" or "PROJECT") || kind == "DIRECT" && actor == target) throw new ArgumentException("CHAT_INVALID_TARGET");
        long id;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            // Serialize lazy creation before taking unique-index range locks.
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='chat:create',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51000,'CHAT_CREATE_BUSY',1;", ct);
            if (credentials is not null) await credentials.CheckAsync(actor,ct);
            var first = Math.Min(actor,target); var second = Math.Max(actor,target);
            var c = await db.Set<ChatConversation>().SingleOrDefaultAsync(c => c.Kind == kind && (kind == "DIRECT"
                ? c.FirstUserId == first && c.SecondUserId == second : kind == "TEAM" ? c.TeamId == target : c.ProjectId == target), ct);
            if (c is null)
            {
                c = new() { Kind = kind, TeamId = kind == "TEAM" ? target : null, ProjectId = kind == "PROJECT" ? target : null,
                    FirstUserId = kind == "DIRECT" ? first : null, SecondUserId = kind == "DIRECT" ? second : null, CreatedAt = Now, UpdatedAt = Now };
                var eligible = await access.Eligible(c, ct);
                if (!eligible.ContainsKey(actor) || kind == "DIRECT" && !await access.Active(target, ct)) throw Hidden();
                db.Set<ChatConversation>().Add(c);
                await db.SaveChangesAsync(ct);
                await Sync(c, ct);
                Event(c, actor, "ConversationChanged", "CHAT_CREATED");
                await db.SaveChangesAsync(ct);
            }
            else if (!await access.CanAccessAsync(actor, c.Id, ct)) throw Hidden();
            id = c.Id;
            await tx.CommitAsync(ct);
        }
        wake.Pulse();
        return await DetailAsync(actor,id,ct);
    }

    public Task<ChatConversationDto> DetailAsync(long actor, long id, CancellationToken ct) =>
        InConversation(actor,id,false,async c => (await Summaries(actor,[c],ct)).Single(),ct);

    public async Task<ChatPage<ChatConversationDto>> InboxAsync(long actor, string? cursor, int size, CancellationToken ct)
    {
        await Enabled(actor, ct); Size(size);
        var query = access.Allowed(actor).AsNoTracking();
        if (cursor is not null)
        {
            var parts = cursor.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || ticks < 0 || ticks > DateTime.MaxValue.Ticks) throw new ArgumentException("CHAT_INVALID_CURSOR");
            var time = new DateTime(ticks, DateTimeKind.Utc); var id = Id(parts[1]);
            query = query.Where(c => c.UpdatedAt < time || c.UpdatedAt == time && c.Id < id);
        }
        var rows = await query.OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.Id).Take(size+1).ToListAsync(ct);
        var page = rows.Take(size).ToArray();
        return new(await Summaries(actor,page,ct), rows.Count>size ? $"{page[^1].UpdatedAt.Ticks}:{page[^1].Id}" : null,rows.Count>size);
    }

    private async Task<IReadOnlyList<ChatConversationDto>> Summaries(long actor, IReadOnlyList<ChatConversation> rows, CancellationToken ct)
    {
        var ids = rows.Select(c=>c.Id).ToArray();
        var facts = await db.Set<ChatConversation>().Where(c=>ids.Contains(c.Id)).Select(c=>new {
            c.Id,
            Title = c.Kind=="DIRECT" ? db.Users.Where(u=>u.Id==(c.FirstUserId==actor?c.SecondUserId:c.FirstUserId)).Select(u=>u.FullName).FirstOrDefault()
                : c.Kind=="TEAM" ? db.Teams.Where(t=>t.Id==c.TeamId).Select(t=>t.Name).FirstOrDefault() : db.Projects.Where(p=>p.Id==c.ProjectId).Select(p=>p.Title).FirstOrDefault(),
            Floor = db.Set<ChatMembershipInterval>().Where(m=>m.ConversationId==c.Id && m.UserId==actor && m.LeftAt==null).Select(m=>(long?)m.FromSequence).FirstOrDefault(),
            Read = db.Set<ChatMemberState>().Where(m=>m.ConversationId==c.Id && m.UserId==actor).Select(m=>(long?)m.LastReadSequence).FirstOrDefault()
        }).ToListAsync(ct);
        var messageFacts = await db.Set<ChatMessage>().Where(m=>ids.Contains(m.ConversationId)
                && db.Set<ChatMembershipInterval>().Any(i=>i.ConversationId==m.ConversationId && i.UserId==actor && i.LeftAt==null && i.FromSequence<=m.Sequence)
                && m.SenderId!=actor && m.RecalledAt==null
                && !db.Set<ChatMemberState>().Any(s=>s.ConversationId==m.ConversationId && s.UserId==actor && s.LastReadSequence>=m.Sequence))
            .GroupBy(m=>m.ConversationId).Select(g=>new { Id=g.Key, Unread=g.LongCount() }).ToListAsync(ct);
        var last = await db.Set<ChatMessage>().AsNoTracking().Where(m=>ids.Contains(m.ConversationId)
            && db.Set<ChatMembershipInterval>().Any(i=>i.ConversationId==m.ConversationId && i.UserId==actor && i.LeftAt==null && i.FromSequence<=m.Sequence)
            && !db.Set<ChatMessage>().Any(n=>n.ConversationId==m.ConversationId && n.Sequence>m.Sequence)).ToListAsync(ct);
        // Only project the last row when its sequence is in this actor's visibility interval.
        var visibleLast = last.Where(m=>facts.Any(f=>f.Id==m.ConversationId && f.Floor.HasValue && m.Sequence>=f.Floor)).ToArray();
        var dto = await Map(actor,visibleLast,ct);
        var stillAllowed=await access.Allowed(actor).Where(c=>ids.Contains(c.Id)).Select(c=>c.Id).ToListAsync(ct);
        return rows.Where(c=>stillAllowed.Contains(c.Id)).Select(c=>new ChatConversationDto(S(c.Id),c.Kind,facts.Single(f=>f.Id==c.Id).Title??"Conversation",c.Status,
            c.TeamId.HasValue?S(c.TeamId.Value):null,c.ProjectId.HasValue?S(c.ProjectId.Value):null,S(c.Sequence),S(c.Version),Utc(c.UpdatedAt),
            messageFacts.FirstOrDefault(f=>f.Id==c.Id)?.Unread??0,dto.FirstOrDefault(m=>m.ConversationId==S(c.Id)),c.Status=="OPEN")).ToArray();
    }

    public Task<ChatPage<ChatPerson>> MembersAsync(long actor,long id,string? cursor,int size,CancellationToken ct) =>
        InConversation(actor,id,false,async _ => {
            Size(size); var after=cursor is null?0:Id(cursor);
            var eligible=(await access.RecipientsAsync(id,ct)).ToArray();
            var people=await db.Users.Where(u=>u.Id>after && eligible.Contains(u.Id)).OrderBy(u=>u.Id).Take(size+1)
                .Select(u=>new { u.Id,u.FullName, Read=db.Set<ChatMemberState>().Where(s=>s.ConversationId==id && s.UserId==u.Id).Select(s=>(long?)s.LastReadSequence).FirstOrDefault() }).ToListAsync(ct);
            return new ChatPage<ChatPerson>(people.Take(size).Select(u=>new ChatPerson(S(u.Id),u.FullName,S(u.Read??0))).ToList(),people.Count>size?S(people[size-1].Id):null,people.Count>size);
        },ct);

    public Task<ChatPage<ChatMessageDto>> MessagesAsync(long actor,long id,string? before,string? after,int size,CancellationToken ct) =>
        InConversation(actor,id,false,async _ => {
            Size(size); if(before is not null && after is not null) throw new ArgumentException("CHAT_INVALID_CURSOR");
            var q=Visible(actor,id).AsNoTracking();
            if(before is not null) { var n=Cursor(before,id); q=q.Where(m=>m.Sequence<n); }
            if(after is not null) { var n=Cursor(after,id); q=q.Where(m=>m.Sequence>n); }
            var rows=await (after is null?q.OrderByDescending(m=>m.Sequence):q.OrderBy(m=>m.Sequence)).Take(size+1).ToListAsync(ct);
            var page=rows.Take(size).ToArray();
            return new ChatPage<ChatMessageDto>(await Map(actor,page.OrderBy(m=>m.Sequence).ToArray(),ct),rows.Count>size?$"{id}:{page[^1].Sequence}":null,rows.Count>size);
        },ct);

    private static long Cursor(string value,long conversation)
    {
        var p=value.Split(':'); if(p.Length!=2 || Id(p[0])!=conversation) throw new ArgumentException("CHAT_INVALID_CURSOR");
        return Id(p[1]);
    }
    private void Text(string body) { if(string.IsNullOrWhiteSpace(body)||body.Length>settings.Value.MaxMessageLength||body.IndexOf('\0')>=0) throw new ArgumentException("CHAT_INVALID_BODY"); }
    public Task<ChatMessageDto> SendAsync(long actor,long id,ChatSendRequest request,CancellationToken ct) =>
        InConversation(actor,id,true,async c => {
            Text(request.Body); if(request.ClientMessageId==Guid.Empty) throw new ArgumentException("CHAT_CLIENT_MESSAGE_ID_REQUIRED");
            long? reply=request.ReplyToMessageId is null?null:Id(request.ReplyToMessageId);
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { request.Body, Reply=reply }))));
            var existing=await db.Set<ChatMessage>().SingleOrDefaultAsync(m=>m.ConversationId==id && m.SenderId==actor && m.ClientMessageId==request.ClientMessageId,ct);
            if(existing is not null) {
                if(existing.RequestHash!=hash) throw new ConflictException("Retry key has different content.","CHAT_IDEMPOTENCY_CONFLICT");
                if(!await Visible(actor,id).AnyAsync(m=>m.Id==existing.Id,ct)) throw Hidden();
                return (await Map(actor,[existing],ct)).Single();
            }
            if(reply.HasValue && !await Visible(actor,id).AnyAsync(m=>m.Id==reply && m.RecalledAt==null,ct)) throw Hidden();
            var m=new ChatMessage { ConversationId=id,SenderId=actor,Sequence=++c.Sequence,ClientMessageId=request.ClientMessageId,
                RequestHash=hash,ReplyToMessageId=reply,Body=request.Body,CreatedAt=Now };
            db.Set<ChatMessage>().Add(m); Event(c,actor,"MessagesChanged","CHAT_SENT");
            await db.SaveChangesAsync(ct); return (await Map(actor,[m],ct)).Single();
        },ct);

    public Task<ChatMessageDto> EditAsync(long actor,long id,long messageId,ChatEditRequest request,CancellationToken ct) =>
        Change(actor,id,messageId,request.ConcurrencyToken,request.Body,false,ct);
    public Task<ChatMessageDto> RecallAsync(long actor,long id,long messageId,ChatRecallRequest request,CancellationToken ct) =>
        Change(actor,id,messageId,request.ConcurrencyToken,null,true,ct);
    private Task<ChatMessageDto> Change(long actor,long id,long messageId,Guid token,string? body,bool recall,CancellationToken ct) =>
        InConversation(actor,id,true,async c=> {
            var m=await Visible(actor,id).SingleOrDefaultAsync(m=>m.Id==messageId,ct)??throw Hidden();
            if(m.SenderId!=actor) throw new ForbiddenException("Only the sender can change a message.","CHAT_SENDER_REQUIRED");
            if(m.ConcurrencyToken!=token) throw new ConflictException("Message changed.","CHAT_STALE");
            if(m.RecalledAt.HasValue || Now>m.CreatedAt.AddMinutes(recall?settings.Value.RecallWindowMinutes:settings.Value.EditWindowMinutes))
                throw new ConflictException("Message can no longer be changed.","CHAT_CHANGE_WINDOW_CLOSED");
            if(recall) { m.Body=null; m.RecalledAt=Now; } else { Text(body!);m.Body=body;m.EditedAt=Now; }
            m.ConcurrencyToken=Guid.NewGuid(); Event(c,actor,"MessagesChanged",recall?"CHAT_RECALLED":"CHAT_EDITED");
            await db.SaveChangesAsync(ct); return (await Map(actor,[m],ct)).Single();
        },ct);

    public async Task ReadAsync(long actor,long id,long messageId,CancellationToken ct) =>
        await InConversation(actor,id,false,async c=> {
            var m=await Visible(actor,id).AsNoTracking().SingleOrDefaultAsync(m=>m.Id==messageId,ct)??throw Hidden();
            var state=await db.Set<ChatMemberState>().FindAsync([id,actor],ct);
            if(state is null) { state=new(){ConversationId=id,UserId=actor};db.Set<ChatMemberState>().Add(state); }
            if(state.LastReadSequence<m.Sequence) { state.LastReadSequence=m.Sequence;state.UpdatedAt=Now;Event(c,actor,"ReadStateChanged",null); }
            return true;
        },ct);

    private void Event(ChatConversation c,long actor,string type,string? audit)
    {
        c.Version++; c.ConcurrencyToken=Guid.NewGuid(); if(type!="ReadStateChanged") c.UpdatedAt=Now;
        db.Set<ChatOutbox>().Add(new() { ConversationId=c.Id,Version=c.Version,EventType=type,CreatedAt=Now,NextAttemptAt=Now });
        if(audit is not null) db.AuditLogs.Add(new(){ ActorUserId=actor,Action=audit,EntityType="CHAT_CONVERSATION",EntityId=S(c.Id),Outcome="SUCCESS",OccurredAt=Now });
    }
    private async Task<IReadOnlyList<ChatMessageDto>> Map(long actor,IReadOnlyList<ChatMessage> rows,CancellationToken ct)
    {
        var senders=rows.Select(m=>m.SenderId).Distinct().ToArray();
        var conversationIds=rows.Select(m=>m.ConversationId).Distinct().ToArray();
        var writable=await db.Set<ChatConversation>().Where(c=>conversationIds.Contains(c.Id)&&c.Status=="OPEN").Select(c=>c.Id).ToListAsync(ct);
        var names=await db.Users.Where(u=>senders.Contains(u.Id)).ToDictionaryAsync(u=>u.Id,u=>u.FullName,ct);
        var replyIds=rows.Where(m=>m.ReplyToMessageId.HasValue).Select(m=>m.ReplyToMessageId!.Value).ToArray();
        var replies=await db.Set<ChatMessage>().AsNoTracking().Where(m=>replyIds.Contains(m.Id)
            && db.Set<ChatMembershipInterval>().Any(i=>i.UserId==actor && i.ConversationId==m.ConversationId && i.LeftAt==null && i.FromSequence<=m.Sequence)).ToDictionaryAsync(m=>m.Id,ct);
        return rows.Select(m=>new ChatMessageDto(S(m.Id),S(m.ConversationId),S(m.Sequence),S(m.SenderId),names.GetValueOrDefault(m.SenderId,"Member"),m.ClientMessageId,
            m.Body,Utc(m.CreatedAt),m.EditedAt.HasValue?Utc(m.EditedAt.Value):null,m.RecalledAt.HasValue?Utc(m.RecalledAt.Value):null,m.ConcurrencyToken,
            m.ReplyToMessageId is long r ? replies.TryGetValue(r,out var parent)&&parent.RecalledAt is null?new(S(r),parent.Body,false):new(S(r),null,true):null,
            writable.Contains(m.ConversationId)&&m.SenderId==actor&&m.RecalledAt is null&&Now<=m.CreatedAt.AddMinutes(settings.Value.EditWindowMinutes),
            writable.Contains(m.ConversationId)&&m.SenderId==actor&&m.RecalledAt is null&&Now<=m.CreatedAt.AddMinutes(settings.Value.RecallWindowMinutes))).ToArray();
    }
    private static DateTime Utc(DateTime date)=>DateTime.SpecifyKind(date,DateTimeKind.Utc);
}
