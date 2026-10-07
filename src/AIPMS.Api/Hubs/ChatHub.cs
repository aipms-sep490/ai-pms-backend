using System.Globalization;
using AIPMS.Application.Features.Chat.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AIPMS.Api.Hubs;

[Authorize]
public sealed class ChatHub(IChatAccessService access, ChatConnections connections, ChatDelivery delivery,
    IChatService chat) : Hub
{
    private ChatConnection Connection => connections.Find(Context.ConnectionId) ?? throw new HubException("CHAT_SESSION_EXPIRED");
    private static long Parse(string id) => long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : throw new HubException("CHAT_INVALID_ID");
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var removed = connections.Remove(Context.ConnectionId);
        if (removed?.Watching is long id)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await delivery.PresenceAsync(id, timeout.Token); } catch (OperationCanceledException) { }
        }
        await base.OnDisconnectedAsync(exception);
    }
    public async Task WatchConversation(string conversationId)
    {
        var id = Parse(conversationId);
        if (!await access.CanAccessAsync(Connection.UserId, id, Context.ConnectionAborted)) throw new HubException("CHAT_NOT_FOUND");
        Connection.Watching = id;
        await delivery.PresenceAsync(id, Context.ConnectionAborted);
    }
    public Task UnwatchConversation(string conversationId)
    {
        if (Connection.Watching == Parse(conversationId)) Connection.Watching = null;
        return Task.CompletedTask;
    }
    public async Task Ping()
    {
        if (Connection.Watching is not long id) return;
        if (!await access.CanAccessAsync(Connection.UserId,id,Context.ConnectionAborted))
        {
            Connection.Watching = null;
            await Clients.Caller.SendAsync("AccessRevoked",new { schemaVersion=1,conversationId=id.ToString(CultureInfo.InvariantCulture) },Context.ConnectionAborted);
            return;
        }
        await delivery.PresenceAsync(id,Context.ConnectionAborted);
    }
    public async Task SetTyping(string conversationId, bool isTyping = true)
    {
        var id = Parse(conversationId);
        if (Connection.Watching != id) throw new HubException("CHAT_NOT_FOUND");
        if (!connections.CanType(Connection.UserId,id)) return;
        var detail = await chat.DetailAsync(Connection.UserId,id,Context.ConnectionAborted);
        if (!detail.CanSend) throw new HubException("CHAT_READ_ONLY");
        await delivery.SendAsync(id,"TypingChanged",new { schemaVersion=1,conversationId,userId=Connection.UserId.ToString(CultureInfo.InvariantCulture),isTyping },Context.ConnectionAborted);
    }
}
