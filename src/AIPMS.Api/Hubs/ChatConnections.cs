using System.Security.Claims;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Chat;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Hubs;

public sealed class ChatConnection(string id,long userId,long passwordTicks,DateTimeOffset expires,Action abort)
{
    public string Id { get; }=id;
    public long UserId { get; }=userId;
    public long PasswordTicks { get; }=passwordTicks;
    public DateTimeOffset Expires { get; }=expires;
    public DateTimeOffset Touched { get; set; }
    public long? Watching { get; set; }
    public Action Abort { get; }=abort;
}
public sealed class ChatConnections(TimeProvider clock,IOptions<ChatSettings> options)
{
    private readonly object gate=new();
    private readonly Dictionary<string,ChatConnection> connections=[];
    private readonly Dictionary<long,(DateTimeOffset At,int Count)> rate=[];
    private readonly Dictionary<(long User,long Conversation),DateTimeOffset> typing=[];
    public ChatConnection Add(HubCallerContext context)
    {
        if(!long.TryParse(context.User?.FindFirstValue(ClaimTypes.NameIdentifier),out var user)||user<=0
            ||!long.TryParse(context.User?.FindFirstValue("pwd"),out var pwd)||pwd<0||pwd>DateTime.MaxValue.Ticks
            ||!long.TryParse(context.User?.FindFirstValue("exp"),out var exp)) throw new HubException("CHAT_SESSION_EXPIRED");
        lock(gate)
        {
            Prune();
            if(connections.Values.Count(c=>c.UserId==user)>=options.Value.MaxConnectionsPerUser) throw new HubException("CHAT_CONNECTION_LIMIT");
            var c=new ChatConnection(context.ConnectionId,user,pwd,DateTimeOffset.FromUnixTimeSeconds(exp),context.Abort){Touched=clock.GetUtcNow()};
            connections.Add(c.Id,c);return c;
        }
    }
    public ChatConnection? Find(string id) { lock(gate) return connections.GetValueOrDefault(id); }
    public ChatConnection? Remove(string id) { lock(gate) { if(!connections.Remove(id,out var c)) return null;return c; } }
    public ChatConnection[] Snapshot() { lock(gate) { Prune();return connections.Values.ToArray(); } }
    public bool Touch(string id)
    {
        lock(gate)
        {
            Prune();if(!connections.TryGetValue(id,out var c)) return false;
            var now=clock.GetUtcNow();var counter=rate.GetValueOrDefault(c.UserId);
            if(now-counter.At>=TimeSpan.FromMinutes(1)) counter=(now,0);
            rate[c.UserId]=(counter.At,counter.Count+1);
            if(counter.Count>=120) throw new HubException("CHAT_RATE_LIMITED");
            c.Touched=now;return true;
        }
    }
    public bool CanType(long user,long conversation)
    {
        lock(gate)
        {
            var now=clock.GetUtcNow();var key=(user,conversation);
            if(typing.TryGetValue(key,out var last)&&now-last<TimeSpan.FromSeconds(3)) return false;
            typing[key]=now;return true;
        }
    }
    private void Prune()
    {
        var now=clock.GetUtcNow();
        foreach(var c in connections.Values.Where(c=>c.Expires<=now || now-c.Touched>TimeSpan.FromSeconds(60)).ToArray())
        { connections.Remove(c.Id);c.Abort(); }
        foreach(var key in rate.Where(x=>now-x.Value.At>TimeSpan.FromMinutes(2)).Select(x=>x.Key).ToArray()) rate.Remove(key);
        foreach(var key in typing.Where(x=>now-x.Value>TimeSpan.FromSeconds(10)).Select(x=>x.Key).ToArray()) typing.Remove(key);
    }
}
internal sealed class ChatHubFilter(ChatConnections connections,IOptions<ChatSettings> options) : IHubFilter
{
    private async Task Validate(HubCallerContext context,IServiceProvider services)
    {
        if(!options.Value.Enabled||!options.Value.RealtimeEnabled) throw new HubException("CHAT_DISABLED");
        var c=connections.Find(context.ConnectionId)??throw new HubException("CHAT_SESSION_EXPIRED");
        if(!connections.Touch(c.Id)||!await services.GetRequiredService<IAccessTokenAccountValidator>().IsValidAsync(c.UserId,
            c.PasswordTicks==0?null:new DateTime(c.PasswordTicks,DateTimeKind.Utc),context.ConnectionAborted))
        { context.Abort();connections.Remove(c.Id);throw new HubException("CHAT_SESSION_EXPIRED"); }
    }
    public async Task OnConnectedAsync(HubLifetimeContext context,Func<HubLifetimeContext,Task> next)
    {
        connections.Add(context.Context);
        try { await Validate(context.Context,context.ServiceProvider);await next(context); }
        catch { connections.Remove(context.Context.ConnectionId);throw; }
    }
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,Func<HubInvocationContext,ValueTask<object?>> next)
    {
        await Validate(context.Context,context.ServiceProvider);
        try { return await next(context); }
        catch(AIPMS.Application.Common.Exceptions.NotFoundException) { throw new HubException("CHAT_NOT_FOUND"); }
        catch(AIPMS.Application.Common.Exceptions.UnauthorizedException) { context.Context.Abort();throw new HubException("CHAT_SESSION_EXPIRED"); }
    }
}
